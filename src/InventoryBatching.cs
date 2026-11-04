using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PerformanceFixes
{
    // Fix 3: batches inventory re-layouts to one per inventory per frame.
    //
    // GameItem.ValidateShapeState runs whenever an item enters or leaves an inventory and on every tag change
    // (EnableTag, DisableTag, ModifyTag). It lays out the item, then calls Validate on the item's whole inventory,
    // which lays out every item in it again. Adding n items is n x n layouts; a save load ran 161,804 item layouts
    // and 7,000 inventory layouts in one frame (about 1.8 s), and a single tag change on an item in a full storage
    // bay re-lays out the whole bay.
    //
    // While ValidateShapeState runs, the inventory re-layouts it asks for are only recorded; the item's own layout
    // and everything else still happen at once. The recorded inventories are laid out once, just before Unity
    // renders the frame (Canvas.willRenderCanvases), so the screen is the same, but repeated requests in one frame
    // cost one layout. An inventory laid out directly in the meantime is struck from the list.
    static class InventoryBatching
    {
        public static bool Enabled = true;

        // Which layouts wait for the end of the frame (its own file, with tests).
        static readonly LayoutBatch<GameInventory> layouts = new LayoutBatch<GameInventory>();
        // Reused buffer for the flush, so a frame allocates nothing for it.
        static readonly List<GameInventory> batch = new List<GameInventory>();

        // Statistics for the long-frame log.
        public static int Deferred, Flushed, Skipped, Failed;

        // Enter is a prefix and Leave a finalizer on ValidateShapeState, so the scope is closed even if the game's
        // method throws. Flush is hooked to the frame end (see the header for why that moment).
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(typeof(GameItem).GetMethod("ValidateShapeState", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(InventoryBatching).GetMethod("Enter", PerformanceMod.AnyStatic)), null, null,
                new HarmonyMethod(typeof(InventoryBatching).GetMethod("Leave", PerformanceMod.AnyStatic)));

            // GameInventory.Validate is abstract; each concrete inventory has its own (one has two), so patch every
            // parameterless Validate with a body that the type itself declares.
            var defer = new HarmonyMethod(typeof(InventoryBatching).GetMethod("ValidatePrefix", PerformanceMod.AnyStatic));
            PatchValidate(harmony, typeof(GameGridInventory).GetMethods(Declared), defer);
            PatchValidate(harmony, typeof(GameGridScrollableInventory).GetMethods(Declared), defer);
            PatchValidate(harmony, typeof(GameSlotInventory).GetMethods(Declared), defer);
            PatchValidate(harmony, typeof(GameCharacterRaidInventory).GetMethods(Declared), defer);

            Canvas.willRenderCanvases += Flush;
        }

        // DeclaredOnly: the override of each concrete type is patched, not the inherited one twice.
        const System.Reflection.BindingFlags Declared = PerformanceMod.AnyInstance | System.Reflection.BindingFlags.DeclaredOnly;

        // GetMethodBody() != null skips methods without IL (extern or abstract stubs), which Harmony cannot patch.
        static void PatchValidate(Harmony harmony, System.Reflection.MethodInfo[] methods, HarmonyMethod prefix)
        {
            foreach (var m in methods)
                if (m.Name == "Validate" && m.GetParameters().Length == 0 && !m.IsAbstract && m.GetMethodBody() != null)
                    harmony.Patch(m, prefix);
        }

        // Lays out what is still waiting, so switching the mod off leaves no inventory unlaid.
        public static void Unpatch()
        {
            Canvas.willRenderCanvases -= Flush;
            Flush();
        }

        // The store has been left: inventories still waiting for a layout are gone.
        public static void Forget() { layouts.Discard(); batch.Clear(); }

        static void Enter() { if (Enabled) layouts.Enter(); }

        // A finalizer, so an exception inside the game's method cannot leave batching switched on.
        static Exception Leave(Exception __exception)
        {
            layouts.Leave();
            return __exception;
        }

        // Skips the game's Validate when the layout is deferred; runs it when called outside ValidateShapeState.
        static bool ValidatePrefix(GameInventory __instance)
        {
            bool newlyDeferred;
            bool now = layouts.Request(__instance, out newlyDeferred);
            if (newlyDeferred) Deferred++;
            return now;
        }

        // Runs before Unity renders the frame: lays out each recorded inventory once.
        static void Flush()
        {
            if (!layouts.Take(batch)) return;
            try
            {
                foreach (var inventory in batch)
                {
                    // An inventory whose UI was destroyed since (a temporary one during loading, say) has nothing
                    // left to lay out.
                    var element = inventory as PixelElement;
                    if (element != null && element.handler == null) { Skipped++; continue; }
                    try { inventory.Validate(); Flushed++; }
                    catch (Exception e)
                    {
                        Failed++;
                        if (Failed == 1)
                            Debug.LogWarning("[PerformanceFixes] deferred inventory layout failed (later failures are only counted): " + e);
                    }
                }
            }
            finally { layouts.Done(); batch.Clear(); }
        }
    }
}
