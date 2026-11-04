using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PerformanceFixes
{
    // Fix 6: a container window is hidden by switching off its canvas instead of deactivating it.
    //
    // PixelWindow.Hide deactivates the window's GameObject and Show activates it again. For a full storage window
    // that is thousands of components being disabled and re-enabled, measured at about 110 ms for one large window
    // either way. Switching off the window's own canvas (fix 2) hides it just as well and costs nothing: nothing is
    // drawn and its raycaster is off, so it takes no clicks. The game only ever reads a window's state through
    // PixelWindow.IsVisible (activeSelf of its root) and only changes it in Show and Hide, so:
    //   - Hide is replaced, for windows that have their own canvas and stay under the floating canvas (fix 4), by
    //     the same steps with the canvas switched off instead of the object deactivated;
    //   - Show switches the canvas back on first, after which its SetActive(true) finds the object already active
    //     and does nothing; for a locked window the game's Show returns at once and the window stays hidden, and so
    //     does this;
    //   - IsVisible answers false for a window hidden this way.
    static class FastHide
    {
        public static bool Enabled = true;

        // The window root objects hidden by canvas. Only objects of the store's scene go in; Forget and DestroyPrefix
        // drop the destroyed ones (code_standard: nothing kept after leaving the store).
        static readonly HashSet<GameObject> hidden = new HashSet<GameObject>();

        // Show is patched on every overload declared in PixelWindow, as the game has several and any of them can
        // bring a hidden window back.
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(typeof(PixelWindow).GetMethod("Hide", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(FastHide).GetMethod("HidePrefix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(PixelWindow).GetMethod("IsVisible", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                null, new HarmonyMethod(typeof(FastHide).GetMethod("IsVisiblePostfix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(PixelWindow).GetMethod("Destroy", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(FastHide).GetMethod("DestroyPrefix", PerformanceMod.AnyStatic)));
            // Show: switch the canvas back on before the game's own Show runs.
            var show = new HarmonyMethod(typeof(FastHide).GetMethod("ShowPrefix", PerformanceMod.AnyStatic));
            foreach (var m in typeof(PixelWindow).GetMethods(PerformanceMod.AnyInstance))
                if (m.Name == "Show" && m.DeclaringType == typeof(PixelWindow)) harmony.Patch(m, show);
        }

        // Everything the game's Hide does, in the same order, except deactivating the object.
        static bool HidePrefix(PixelWindow __instance, ref PixelWindow __result)
        {
            if (!Enabled || !KeepAdopted.WillKeep(__instance)) return true;
            var handler = __instance.handler;
            if (handler == null) return true;
            var go = handler.gameObject;
            var canvas = go.GetComponent<Canvas>();
            if (canvas == null || !go.activeSelf) return true;

            var owners = __instance.parentItems;
            if (owners != null)
                foreach (var item in owners) item.RememberContentWindowPositionIfMine(__instance);
            canvas.enabled = false;
            var raycaster = go.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;
            hidden.Add(go);
            UIFamilyLayer.ReleaseWindow(__instance);
            WindowsHandler.TryRemoveWindow(__instance);
            __result = __instance;
            return false;
        }

        // The game's Show returns at once for a locked window (a container locked while closed), leaving it hidden;
        // so does this.
        // Prefix, not postfix: the game's Show calls SetActive(true), which must find the object already active.
        static void ShowPrefix(PixelWindow __instance)
        {
            if (__instance.isLocked) return;
            var handler = __instance.handler;
            if (handler == null) return;
            var go = handler.gameObject;
            if (!hidden.Remove(go)) return;
            var canvas = go.GetComponent<Canvas>();
            if (canvas != null) canvas.enabled = true;
            var raycaster = go.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = true;
        }

        // Keeps the game's view of the window (closed) in line with what the player sees.
        static void IsVisiblePostfix(PixelWindow __instance, ref bool __result)
        {
            if (!__result || hidden.Count == 0) return;
            var handler = __instance.handler;
            if (handler != null && hidden.Contains(handler.gameObject)) __result = false;
        }

        // A window about to be destroyed is no longer listed here. The game's Destroy then hides it with its own Hide
        // (KeepAdopted keeps no window while one is destroyed, so this mod's Hide steps aside) and destroys it.
        static void DestroyPrefix(PixelWindow __instance)
        {
            var handler = __instance.handler;
            if (handler != null) hidden.Remove(handler.gameObject);
            if (hidden.Count > 256) hidden.RemoveWhere(g => g == null);
        }

        // Switching the fix off makes every window hidden this way an ordinary hidden window again.
        public static void RestoreAll()
        {
            foreach (var go in hidden)
            {
                if (go == null) continue;
                var canvas = go.GetComponent<Canvas>();
                if (canvas != null) canvas.enabled = true;
                var raycaster = go.GetComponent<GraphicRaycaster>();
                if (raycaster != null) raycaster.enabled = true;
                go.SetActive(false);
            }
            hidden.Clear();
        }

        // Shown in the long-frame line.
        public static int Count { get { return hidden.Count; } }

        // The store has been left: drop its windows, which are gone.
        public static void Forget() { hidden.RemoveWhere(g => g == null); }
    }
}
