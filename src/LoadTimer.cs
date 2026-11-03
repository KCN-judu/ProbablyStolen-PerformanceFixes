using System;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PerformanceFixes
{
    // Diagnostics: times each trip from the main menu into the store and writes one line to Player.log.
    //
    // Loading a save or starting a new game (MainMenuUIController.StartNewGameClick) plays a black-screen fade
    // (StartBlackscreenSequence) and, when the screen is black, runs its action, which switches to the store with the
    // synchronous SceneManager.LoadScene. The line gives, from the button press: the fade reaching black, the scene set
    // up (SceneManager.sceneLoaded), the save load starting and ending (from LoadChecks) and the first frame drawn
    // after it. The game's own action runs unchanged; it is only wrapped to note when it starts. The steps are kept by
    // LoadTimeline (its own file, with tests).
    static class LoadTimer
    {
        const string StoreScene = "InventoryScene";
        const float GiveUpAfter = 120f;

        static readonly LoadTimeline timeline = new LoadTimeline();
        static readonly Stopwatch clock = new Stopwatch();
        static bool inClick;
        static int busyFrame = -1;

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(typeof(MainMenuUIController).GetMethod("StartNewGameClick", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(LoadTimer).GetMethod("ClickPrefix", PerformanceMod.AnyStatic)), null, null,
                new HarmonyMethod(typeof(LoadTimer).GetMethod("ClickFinalizer", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(MainMenuUIController).GetMethod("StartBlackscreenSequence", PerformanceMod.AnyInstance, null,
                    new[] { typeof(bool), typeof(Action), typeof(Action), typeof(float), typeof(float) }, null),
                new HarmonyMethod(typeof(LoadTimer).GetMethod("SequencePrefix", PerformanceMod.AnyStatic)));
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public static void Unpatch() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        static void ClickPrefix() { inClick = true; }

        static Exception ClickFinalizer(Exception __exception)
        {
            inClick = false;
            return __exception;
        }

        // The start button's fade begins: start the trip's clock, and note when the fade's black-screen action runs.
        static void SequencePrefix(ref Action actionDuringBlack)
        {
            if (!inClick) return;
            clock.Reset();
            clock.Start();
            timeline.Begin();
            busyFrame = -1;
            var gameAction = actionDuringBlack;
            actionDuringBlack = () =>
            {
                Mark("black");
                if (gameAction != null) gameAction();
            };
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != StoreScene) return;
            Mark("scene set up");
            busyFrame = Time.frameCount;
        }

        // From LoadChecks: the save itself.
        public static void SaveLoadStarted() { Mark("save load started"); }

        public static void SaveLoaded()
        {
            Mark("save loaded");
            busyFrame = Time.frameCount;
        }

        static void Mark(string step)
        {
            if (timeline.Started) timeline.Mark(step, clock.Elapsed.TotalMilliseconds);
        }

        // Every frame, from the mod's ticker. The frame that set up the scene or loaded the save has been drawn once a
        // later frame starts.
        public static void Tick()
        {
            if (!timeline.Started) return;
            if (busyFrame >= 0 && Time.frameCount > busyFrame)
            {
                Mark("first frame drawn");
                Finish();
            }
            else if (clock.Elapsed.TotalSeconds > GiveUpAfter) Finish();
        }

        static void Finish()
        {
            timeline.End();
            clock.Stop();
            UnityEngine.Debug.Log("[PerformanceFixes] load timeline: " + timeline.Report());
        }
    }
}
