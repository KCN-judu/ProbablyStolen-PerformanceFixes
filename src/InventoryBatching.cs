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

        static int depth;
        static bool flushing;
        static readonly HashSet<GameInventory> pending = new HashSet<GameInventory>();
        static readonly List<GameInventory> batch = new List<GameInventory>();

        // Statistics for the long-frame log.
        public static int Deferred, Flushed, Skipped, Failed;

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

        const System.Reflection.BindingFlags Declared = PerformanceMod.AnyInstance | System.Reflection.BindingFlags.DeclaredOnly;

        static void PatchValidate(Harmony harmony, System.Reflection.MethodInfo[] methods, HarmonyMethod prefix)
        {
            foreach (var m in methods)
                if (m.Name == "Validate" && m.GetParameters().Length == 0 && !m.IsAbstract && m.GetMethodBody() != null)
                    harmony.Patch(m, prefix);
        }

        public static void Unpatch()
        {
            Canvas.willRenderCanvases -= Flush;
            Flush();
        }

        static void Enter() { if (Enabled) depth++; }

        // A finalizer, so an exception inside the game's method cannot leave batching switched on.
        static Exception Leave(Exception __exception)
        {
            if (depth > 0) depth--;
            return __exception;
        }

        static bool ValidatePrefix(GameInventory __instance)
        {
            if (depth > 0 && !flushing)
            {
                if (pending.Add(__instance)) Deferred++;
                return false;
            }
            if (!flushing) pending.Remove(__instance);
            return true;
        }

        static void Flush()
        {
            if (pending.Count == 0 || flushing) return;
            flushing = true;
            batch.Clear();
            batch.AddRange(pending);
            pending.Clear();
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
            finally { flushing = false; batch.Clear(); }
        }
    }
}
