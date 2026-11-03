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

        static readonly Queue<Step> queue = new Queue<Step>();
        static readonly Dictionary<GameItem, Step> deferredOpen = new Dictionary<GameItem, Step>();
        static bool inToggle, running, firstDone;

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

        public static void FinishAll()
        {
            while (queue.Count > 0) Run(queue.Dequeue());
        }

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
