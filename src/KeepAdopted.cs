using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PerformanceFixes
{
    // Fix 4: a closed window stays under the floating-window canvas instead of moving back.
    //
    // When a window opens, UIFamilyLayer.Adopt moves it from its home (the scene's window canvas) under the
    // floating-window canvas; when it closes, ReleaseWindow moves it back. Moving a hierarchy between canvases makes
    // every graphic and text in it re-register and rebuild, so a full storage window costs tens of milliseconds
    // each way. The storage-preset hotkey opens or closes four at once, which was measured at about 140 ms of the
    // 280 ms stutter.
    //
    // A hidden window sits inactive, so where it sits does not show. Keeping it under the floating canvas means the
    // next Adopt finds it already there and only reorders it. It is kept only when its home is in the same scene as
    // the floating canvas, so a scene change destroys it together with its home, exactly as before; a window that is
    // destroyed goes home first as usual.
    static class KeepAdopted
    {
        public static bool Enabled = true;

        // Window rect -> the canvas it came from. Holds store objects, so Forget prunes it after leaving the store and
        // Prune runs when it grows past 512 (code_standard: nothing kept after leaving the store).
        static readonly Dictionary<RectTransform, Transform> homes = new Dictionary<RectTransform, Transform>();
        // Nonzero while PixelWindow.Destroy runs: the window must go home then, as in the game, so nothing is kept.
        static int destroying;

        // Adopt records the home; ReleaseWindow is skipped for kept windows; Destroy only flags that a window is being
        // destroyed (the finalizer clears it even on an exception).
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(typeof(UIFamilyLayer).GetMethod("Adopt", PerformanceMod.AnyStatic, null, new[] { typeof(RectTransform) }, null),
                new HarmonyMethod(typeof(KeepAdopted).GetMethod("AdoptPrefix", PerformanceMod.AnyStatic)),
                new HarmonyMethod(typeof(KeepAdopted).GetMethod("AdoptPostfix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(UIFamilyLayer).GetMethod("ReleaseWindow", PerformanceMod.AnyStatic, null, new[] { typeof(PixelWindow) }, null),
                new HarmonyMethod(typeof(KeepAdopted).GetMethod("ReleasePrefix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(PixelWindow).GetMethod("Destroy", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(KeepAdopted).GetMethod("DestroyPrefix", PerformanceMod.AnyStatic)), null, null,
                new HarmonyMethod(typeof(KeepAdopted).GetMethod("DestroyFinalizer", PerformanceMod.AnyStatic)));
        }

        // Remember where the window lived before Adopt moves it.
        static void AdoptPrefix(RectTransform windowRect, out Transform __state)
        {
            __state = windowRect != null ? windowRect.parent : null;
        }

        // Only records when Adopt actually moved the window (parent changed).
        static void AdoptPostfix(RectTransform windowRect, Transform __state)
        {
            if (windowRect == null || __state == null || windowRect.parent == __state) return;
            if (homes.Count > 512) Prune();
            homes[windowRect] = __state;
        }

        // Skips the game's move back home for a window WillKeep accepts.
        static bool ReleasePrefix(PixelWindow window)
        {
            if (!WillKeep(window)) return true;
            KeptCount++;
            return false;
        }

        // Whether closing this window leaves it under the floating canvas (FastHide relies on that).
        public static bool WillKeep(PixelWindow window)
        {
            if (!Enabled || destroying > 0 || window == null) return false;
            var rect = window.rectTransform;
            Transform home;
            if (rect == null || !homes.TryGetValue(rect, out home) || home == null) return false;
            return home.gameObject.scene == rect.gameObject.scene;
        }

        static void DestroyPrefix() { destroying++; }

        static Exception DestroyFinalizer(Exception __exception)
        {
            if (destroying > 0) destroying--;
            return __exception;
        }

        // Windows closed without moving back; for the long-frame line.
        public static int KeptCount;

        static readonly List<RectTransform> dead = new List<RectTransform>();

        // The store has been left: drop the homes of its windows, which are gone.
        public static void Forget() { Prune(); }

        // Removes entries whose window or home has been destroyed (Unity null).
        static void Prune()
        {
            dead.Clear();
            foreach (var pair in homes) if (pair.Key == null || pair.Value == null) dead.Add(pair.Key);
            foreach (var key in dead) homes.Remove(key);
        }
    }
}
