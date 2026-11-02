using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PerformanceFixes
{
    // Fix 2: gives every container window its own canvas.
    //
    // The game puts all floating windows under one canvas (UIFamilyLayer's FloatingWindowCanvas). Unity rebuilds a
    // canvas as a whole: when anything in it changes (a window opens, an item moves, a slot lights up under the
    // mouse), every graphic of every open window is re-batched. With a few large storage windows open that is
    // thousands of graphics per change, which is the stutter when a full container is opened.
    //
    // A nested canvas is rebuilt on its own, so a change inside one window only costs that window. The nested
    // canvas keeps the parent's draw order (no sorting override), takes the root's reference pixels-per-unit and
    // shader channels so sprites and text look the same, and gets a GraphicRaycaster so its contents still receive
    // clicks. The game finds its bounds through Canvas.rootCanvas and positions windows with its own window canvas,
    // so placement and dragging are unaffected.
    static class WindowCanvases
    {
        public static bool Enabled = true;

        public static void Patch(Harmony harmony)
        {
            // Before Show, so the window is already on its own canvas when it turns visible and even the first
            // opening skips the full rebuild.
            var prefix = new HarmonyMethod(typeof(WindowCanvases).GetMethod("ShowPrefix", PerformanceMod.AnyStatic));
            foreach (var m in typeof(PixelWindow).GetMethods(PerformanceMod.AnyInstance))
                if (m.Name == "Show" && m.DeclaringType == typeof(PixelWindow)) harmony.Patch(m, prefix);
        }

        // Container windows only: tooltips and menus are small and live elsewhere.
        static void ShowPrefix(PixelWindow __instance)
        {
            if (!Enabled) return;
            var owners = __instance.parentItems;
            if (owners == null || owners.Count == 0) return;
            var handler = __instance.handler;
            if (handler == null) return;
            Isolate(handler.gameObject);
        }

        public static void Isolate(GameObject go)
        {
            if (go.GetComponent<Canvas>() != null) return;
            var parent = go.transform.parent != null ? go.transform.parent.GetComponentInParent<Canvas>() : null;
            if (parent == null) return;
            var root = parent.rootCanvas;

            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = false;
            canvas.referencePixelsPerUnit = root.referencePixelsPerUnit;
            canvas.additionalShaderChannels = root.additionalShaderChannels;

            var raycaster = go.AddComponent<GraphicRaycaster>();
            var rootRaycaster = root.GetComponent<GraphicRaycaster>();
            if (rootRaycaster != null)
            {
                raycaster.ignoreReversedGraphics = rootRaycaster.ignoreReversedGraphics;
                raycaster.blockingObjects = rootRaycaster.blockingObjects;
            }
            PerformanceMod.Isolated++;
        }
    }
}
