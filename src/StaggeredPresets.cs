using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PerformanceFixes
{
    // Fix 5: the storage-preset hotkey opens or closes its windows one per frame.
    //
    // MachineStorageCoordinator.TogglePresets opens (OpenContentAction, then ApplyContentTabsSelection) or closes
    // (CloseContentWindow) every window of a preset in one frame. Turning thousands of UI objects on or off is
    // Unity's own work and cannot be made cheaper, but done in one frame it freezes every animation for its whole
    // length. Here the first window opens or closes at once, so the key answers immediately, and the rest follow on
    // the next frames, one each, with each window's tab selection applied right after its own opening, as the game
    // does. Pressing the hotkey again while some are pending first finishes those, so the game always sees a settled
    // state.
    static class StaggeredPresets
    {
        public static bool Enabled = true;

        // One window action put off to a later frame, with the arguments of the game's call.
        sealed class Step
        {
            public ItemMouseDoubleClickHandler Opener;
            public GameItem Item;
            public Vector2 Position;
            public string Tabs;
            public bool HasTabs;
            public bool Close;
            public bool IncludeChildren;
        }

        // queue holds the pending steps in order; deferredOpen finds the open step of an item so its tab selection can
        // be attached. Both hold store objects and are emptied by Forget. inToggle is true during TogglePresets; running
        // is true while a step runs, so the game's call made by Run goes through the patches unchanged; firstDone marks
        // that the first window of this toggle has been handled at once.
        static readonly Queue<Step> queue = new Queue<Step>();
        static readonly Dictionary<GameItem, Step> deferredOpen = new Dictionary<GameItem, Step>();
        static bool inToggle, running, firstDone;

        // TogglePresets marks the span in which calls are put off; the open, tab and close methods it calls are the
        // ones intercepted.
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(typeof(MachineStorageCoordinator).GetMethod("TogglePresets", PerformanceMod.AnyStatic),
                new HarmonyMethod(typeof(StaggeredPresets).GetMethod("TogglePrefix", PerformanceMod.AnyStatic)), null, null,
                new HarmonyMethod(typeof(StaggeredPresets).GetMethod("ToggleFinalizer", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(ItemMouseDoubleClickHandler).GetMethod("OpenContentAction", PerformanceMod.AnyInstance, null,
                    new[] { typeof(GameItem), typeof(Vector2) }, null),
                new HarmonyMethod(typeof(StaggeredPresets).GetMethod("OpenPrefix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(GameItem).GetMethod("ApplyContentTabsSelection", PerformanceMod.AnyInstance, null,
                    new[] { typeof(string) }, null),
                new HarmonyMethod(typeof(StaggeredPresets).GetMethod("TabsPrefix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(GameItem).GetMethod("CloseContentWindow", PerformanceMod.AnyInstance, null,
                    new[] { typeof(bool) }, null),
                new HarmonyMethod(typeof(StaggeredPresets).GetMethod("ClosePrefix", PerformanceMod.AnyStatic)));
        }

        // Finishes steps of an earlier press first, so each press starts from a settled state.
        static void TogglePrefix()
        {
            FinishAll();
            inToggle = Enabled;
            firstDone = false;
        }

        static Exception ToggleFinalizer(Exception __exception)
        {
            inToggle = false;
            deferredOpen.Clear();
            return __exception;
        }

        // Lets the first open run now and queues the rest.
        static bool OpenPrefix(ItemMouseDoubleClickHandler __instance, GameItem newItem, Vector2 mousePosition)
        {
            if (!inToggle || running || newItem == null) return true;
            if (!firstDone) { firstDone = true; return true; }
            var step = new Step { Opener = __instance, Item = newItem, Position = mousePosition };
            queue.Enqueue(step);
            deferredOpen[newItem] = step;
            return false;
        }

        // The tab selection of a window whose opening was put off travels with it.
        static bool TabsPrefix(GameItem __instance, string selection)
        {
            if (!inToggle || running) return true;
            Step step;
            if (!deferredOpen.TryGetValue(__instance, out step)) return true;
            step.Tabs = selection;
            step.HasTabs = true;
            return false;
        }

        // As OpenPrefix, for closing.
        static bool ClosePrefix(GameItem __instance, bool includeChildren)
        {
            if (!inToggle || running) return true;
            if (!firstDone) { firstDone = true; return true; }
            queue.Enqueue(new Step { Item = __instance, Close = true, IncludeChildren = includeChildren });
            return false;
        }

        // Called once per frame.
        public static void Tick()
        {
            if (queue.Count > 0) Run(queue.Dequeue());
        }

        // Runs every pending step now; used before a new toggle and when the option is switched off.
        public static void FinishAll()
        {
            while (queue.Count > 0) Run(queue.Dequeue());
        }

        // Runs one step through the game's own method; a failure is logged and does not stop the queue.
        static void Run(Step step)
        {
            running = true;
            try
            {
                if (step.Item == null) return;
                if (step.Close) step.Item.CloseContentWindow(step.IncludeChildren);
                else if (step.Opener != null)
                {
                    step.Opener.OpenContentAction(step.Item, step.Position);
                    if (step.HasTabs) step.Item.ApplyContentTabsSelection(step.Tabs);
                }
            }
            catch (Exception e) { Debug.LogWarning("[PerformanceFixes] staggered preset window failed: " + e.Message); }
            finally { running = false; }
        }

        // The store has been left: windows still waiting to open or close belong to it and are gone.
        public static void Forget()
        {
            queue.Clear();
            deferredOpen.Clear();
        }
    }
}
