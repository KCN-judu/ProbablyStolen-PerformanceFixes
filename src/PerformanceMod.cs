using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace PerformanceFixes
{
    // Performance fixes for the base game. Each fix keeps the game's behaviour and only removes wasted work, and
    // each has its own option, grouped as in the options screen:
    //   Windows
    //     WindowCanvases    each container window is drawn on its own canvas
    //     KeepAdopted       a closed window stays under the floating-window canvas
    //     FastHide          a closed container window stops being drawn instead of being deactivated
    //                       (needs the two above)
    //     StaggeredPresets  the storage-preset hotkey opens or closes its windows one per frame
    //   Inventories
    //     InventoryBatching an inventory is re-laid out once per frame instead of once per item change
    //     SelectionHighlight the highlight under selected or group-dragged items is updated in place each frame
    //   Customers
    //     BuyerCheck        the buying check on containers runs once per item instead of quadratically
    //   Diagnostics
    //     long-frame log (on) and the long-frame profiler (off), to find the next thing worth fixing
    //
    // A fix either installs completely or not at all: if one of its patches cannot attach, the ones it already
    // attached are removed again, the fix is off, and Player.log gets one line.
    //
    // BuyerCheck is installed only once the player's language is applied. Patching compiles the patched method, and
    // StoreClient.IsClientRefusingItem uses WaterFeatureHelper, whose static constructor reads the localized water
    // names. Mods start before the game applies the player's language, so compiling it then would fix those names
    // in English for the session, and water would stop selling in any other language. Of all the methods this mod
    // patches (the profiler's included), it is the only one that uses such a type.
    public sealed class PerformanceMod : IMod
    {
        public const string ModId = "performance_fixes";
        const string HarmonyId = "mod." + ModId;
        const string Tag = "[PerformanceFixes] ";
        const float LongFrame = 0.1f;

        internal const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static string modId = ModId;
        static Harmony harmony;
        static GameObject ticker;
        static ConfigEntry<bool> isolate, batching, selection, keep, stagger, fastHide, buyer, logFrames, profile;
        static Ticker tickerBehaviour;
        static bool optionsAdded;

        internal static int Isolated;
        static string lastWindow;
        static float lastWindowAt, nextLog;

        // BuyerCheck waits for the language the game resolves at startup to be applied (see WaitForLanguage).
        const float LanguageTimeout = 300f;
        static string startupLocale;
        static bool buyerWaiting;
        static float buyerSince;

        public void Init(ModManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.ID)) modId = manifest.ID;
        }

        public void OnEnable()
        {
            ReadConfig();
            if (ticker == null)
            {
                ticker = new GameObject("PerformanceFixes.Ticker");
                UnityEngine.Object.DontDestroyOnLoad(ticker);
                ticker.hideFlags = HideFlags.HideAndDontSave;
                tickerBehaviour = ticker.AddComponent<Ticker>();
            }
            if (harmony == null)
            {
                // Each fix on its own: one that fails to patch (after a game update, say) leaves the others working.
                harmony = new Harmony(HarmonyId);
                TryPatch("startup language", () => harmony.Patch(typeof(LocHelper).GetMethod("ResolveStartupLocale", AnyStatic, null, Type.EmptyTypes, null),
                    null, new HarmonyMethod(typeof(PerformanceMod).GetMethod("NoteStartupLocale", AnyStatic))));
                buyerWaiting = true;
                buyerSince = Time.realtimeSinceStartup;
                TryPatch("window canvases", () => WindowCanvases.Patch(harmony));
                TryPatch("inventory batching", () => InventoryBatching.Patch(harmony), InventoryBatching.Unpatch);
                TryPatch("selection highlight", () => SelectionHighlight.Patch(harmony));
                TryPatch("keep closed windows", () => KeepAdopted.Patch(harmony));
                TryPatch("staggered presets", () => StaggeredPresets.Patch(harmony));
                TryPatch("fast hide", () => FastHide.Patch(harmony));
                TryPatch("window log", () => harmony.Patch(typeof(PixelWindow).GetMethod("Show", AnyInstance, null,
                        new[] { typeof(Vector2), typeof(Vector2), typeof(bool) }, null),
                    null, new HarmonyMethod(typeof(PerformanceMod).GetMethod("NoteWindow", AnyStatic))));
            }
            if (profile != null && profile.Value) TryPatch("profiler", () => Profiler.Start(tickerBehaviour));
        }

        public void OnDisable()
        {
            Profiler.Stop();
            buyerWaiting = false;
            if (harmony != null) { InventoryBatching.Unpatch(); harmony.UnpatchAll(HarmonyId); harmony = null; }
            if (ticker != null) { UnityEngine.Object.Destroy(ticker); ticker = null; }
        }

        // Installs one fix. If it fails, removes the patches it added (PatchRollback, with tests) and runs undo for
        // anything else it set up, so the fix is fully off and the others keep theirs.
        static void TryPatch(string fix, Action patch, Action undo = null)
        {
            var before = harmony != null ? OwnPatches() : null;
            try { patch(); }
            catch (Exception e)
            {
                string rest = "";
                try
                {
                    if (before != null)
                        foreach (var added in PatchRollback.Added(before, OwnPatches())) harmony.Unpatch(added.Key, added.Value);
                    if (undo != null) undo();
                }
                catch (Exception u) { rest = " (and could not be fully removed: " + u.Message + ")"; }
                Debug.LogWarning(Tag + fix + " is off: " + e.GetType().Name + ": " + e.Message + rest);
            }
        }

        // Every patch this mod has attached, as (patched method, patch method).
        static HashSet<KeyValuePair<MethodBase, MethodInfo>> OwnPatches()
        {
            var patches = new HashSet<KeyValuePair<MethodBase, MethodInfo>>();
            foreach (var method in harmony.GetPatchedMethods())
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                foreach (var list in new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers })
                    foreach (var p in list)
                        if (p.owner == HarmonyId) patches.Add(new KeyValuePair<MethodBase, MethodInfo>(method, p.PatchMethod));
            }
            return patches;
        }

        // The game resolves the player's language once at startup, then applies it.
        static void NoteStartupLocale(string __result) { startupLocale = __result; }

        // Installs BuyerCheck once that language is the selected one (LocaleGate, with tests); gives up after
        // LanguageTimeout, leaving it off for the session.
        static void WaitForLanguage()
        {
            bool initialized = startupLocale != null && LocalizationSettings.InitializationOperation.IsDone;
            string selected = initialized ? LocHelper.GetCurrentLocaleCode() : null;
            switch (LocaleGate.Decide(initialized, startupLocale, selected, Time.realtimeSinceStartup - buyerSince, LanguageTimeout))
            {
                case LocaleGate.State.Ready:
                    buyerWaiting = false;
                    TryPatch("buyer check", () => BuyerCheck.Patch(harmony));
                    break;
                case LocaleGate.State.GiveUp:
                    buyerWaiting = false;
                    Debug.LogWarning(Tag + "buyer check is off: the game's language was not applied within " + LanguageTimeout + " s");
                    break;
            }
        }

        static string L(string key) { return ModHelper.GetLocalized(modId, key); }

        static void ReadConfig()
        {
            if (optionsAdded) return;
            optionsAdded = true;
            try
            {
                // Keys are unchanged from earlier builds, so saved choices carry over; sections and order follow the
                // groups above.
                var config = ModHelper.GetConfig(modId);
                isolate = config.Define("isolateWindows", true, "Windows", () => L("opt_isolate"), () => L("opt_isolate_tip"), false);
                WindowCanvases.Enabled = isolate.Value;
                isolate.OnChanged += v => WindowCanvases.Enabled = v;
                keep = config.Define("keepClosedWindows", true, "Windows", () => L("opt_keep"), () => L("opt_keep_tip"), false);
                KeepAdopted.Enabled = keep.Value;
                keep.OnChanged += v => KeepAdopted.Enabled = v;
                fastHide = config.Define("fastHideWindows", true, "Windows", () => L("opt_fasthide"), () => L("opt_fasthide_tip"), false);
                FastHide.Enabled = fastHide.Value;
                fastHide.OnChanged += v => { FastHide.Enabled = v; if (!v) FastHide.RestoreAll(); };
                stagger = config.Define("staggerPresetWindows", true, "Windows", () => L("opt_stagger"), () => L("opt_stagger_tip"), false);
                StaggeredPresets.Enabled = stagger.Value;
                stagger.OnChanged += v => { StaggeredPresets.Enabled = v; if (!v) StaggeredPresets.FinishAll(); };
                batching = config.Define("batchInventoryLayout", true, "Inventories", () => L("opt_batch"), () => L("opt_batch_tip"), false);
                InventoryBatching.Enabled = batching.Value;
                batching.OnChanged += v => InventoryBatching.Enabled = v;
                selection = config.Define("fastSelectionHighlight", true, "Inventories", () => L("opt_select"), () => L("opt_select_tip"), false);
                SelectionHighlight.Enabled = selection.Value;
                selection.OnChanged += v => SelectionHighlight.Enabled = v;
                buyer = config.Define("fastBuyerCheck", true, "Customers", () => L("opt_buyer"), () => L("opt_buyer_tip"), false);
                BuyerCheck.Enabled = buyer.Value;
                buyer.OnChanged += v => BuyerCheck.Enabled = v;
                logFrames = config.Define("logLongFrames", true, "Diagnostics", () => L("opt_log"), () => L("opt_log_tip"), false);
                profile = config.Define("profileLongFrames", false, "Diagnostics", () => L("opt_profile"), () => L("opt_profile_tip"), false);
                profile.OnChanged += v => { if (v && tickerBehaviour != null) Profiler.Start(tickerBehaviour); else if (!v) Profiler.Stop(); };
            }
            catch (Exception e) { Debug.LogWarning(Tag + "settings unavailable, using defaults: " + e.Message); }
        }

        static void NoteWindow(PixelWindow __instance)
        {
            var owners = __instance.parentItems;
            if (owners == null || owners.Count == 0) return;
            lastWindow = __instance.titleString;
            lastWindowAt = Time.unscaledTime;
        }

        internal static void Tick()
        {
            try { StaggeredPresets.Tick(); } catch (Exception) { }
            if (buyerWaiting && harmony != null)
                try { WaitForLanguage(); }
                catch (Exception e) { buyerWaiting = false; Debug.LogWarning(Tag + "buyer check is off: " + e.Message); }
            float dt = Time.unscaledDeltaTime;
            if (Profiler.Running || (logFrames != null && !logFrames.Value)) return;
            if (dt < LongFrame || Time.unscaledTime < nextLog || Time.frameCount < 300) return;
            nextLog = Time.unscaledTime + 2f;
            string context = lastWindow != null && Time.unscaledTime - lastWindowAt < 1f
                ? ", right after opening '" + lastWindow + "'" : "";
            Debug.Log(Tag + "long frame: " + (dt * 1000f).ToString("0") + " ms" + context +
                " (windows on their own canvas: " + Isolated + ", inventory layouts batched: " + InventoryBatching.Deferred +
                " into " + InventoryBatching.Flushed + ", skipped " + InventoryBatching.Skipped + ", failed " + InventoryBatching.Failed +
                ", closed windows kept in place: " + KeepAdopted.KeptCount + ", hidden by canvas: " + FastHide.Count + ")");
        }
    }

    public sealed class Ticker : MonoBehaviour
    {
        void Update() { PerformanceMod.Tick(); }
    }
}
