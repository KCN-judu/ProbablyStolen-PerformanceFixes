using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;

namespace PerformanceFixes
{
    // Fix 8: while a save loads, each item's UI is worked out in one pass per part instead of dozens of times.
    //
    // PlayerStore.LoadGame decodes the save in eleven parts, one per inventory (PlayerStore.DecodeSaveItem). Each part
    // builds its items, sets their saved tags, runs about fifteen load steps per item (machines, locks, plants, water
    // sprites and more) and moves the items into the inventory. Every tag change and every move runs
    // GameItem.ValidateShapeState, which computes the item's shape and state and then calls Validate on the item's UI
    // (GameItemElement.Validate), and that in turn validates every item the item is inside; every inventory layout
    // walks all its items' UI again. A large store measured about 58,000 shape checks and 62,000 UI checks for 1,342
    // items, over a second of the load: the same item's UI worked out about 45 times.
    //
    // The UI walks of one part are combined into one, at the end of that same DecodeSaveItem call: each item whose UI
    // was asked for is worked out once, in the order first asked, then the containers around it once each; items
    // destroyed during the part are skipped. Everything that is not the item's UI runs exactly where and as often as in
    // the game: the shape and state, placement, inventory layouts (they also write sizes and slot positions that later
    // shape computations read) and every load step, none of which reads an item's UI. An item's UI is worked out from
    // its current shape, state and contents, so the one pass gives what the game's last walk gives. The bookkeeping is
    // in CombinedPass (its own file, with tests).
    //
    // Each load writes one line to Player.log with the load time and the number of items in each part's inventory, so
    // a load with the option on can be compared with one with it off.
    static class LoadChecks
    {
        public static bool Enabled = true;

        static readonly CombinedPass<GameItemElement> checks = new CombinedPass<GameItemElement>();
        static readonly List<int> counts = new List<int>();
        static readonly Stopwatch load = new Stopwatch(), decode = new Stopwatch();
        static int failed, uncheckedAsked, nodesAtStart, shapesAtStart, walksSkippedAtStart;
        static bool loading;

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(typeof(PlayerStore).GetMethod("LoadGame", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(LoadChecks).GetMethod("LoadPrefix", PerformanceMod.AnyStatic)), null, null,
                new HarmonyMethod(typeof(LoadChecks).GetMethod("LoadFinalizer", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(PlayerStore).GetMethod("DecodeSaveItem", PerformanceMod.AnyInstance, null,
                    new[] { typeof(string), typeof(GameInventory) }, null),
                new HarmonyMethod(typeof(LoadChecks).GetMethod("DecodePrefix", PerformanceMod.AnyStatic)), null, null,
                new HarmonyMethod(typeof(LoadChecks).GetMethod("DecodeFinalizer", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(GameItemElement).GetMethod("Validate", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(LoadChecks).GetMethod("ValidatePrefix", PerformanceMod.AnyStatic)));
        }

        static void LoadPrefix()
        {
            loading = true;
            counts.Clear();
            checks.ResetStatistics();
            failed = uncheckedAsked = 0;
            nodesAtStart = ItemNodes.Built;
            shapesAtStart = ShapeChecks.Computed;
            walksSkippedAtStart = ShapeChecks.WalksSkipped;
            LoadTimer.SaveLoadStarted();
            decode.Reset();
            load.Reset();
            load.Start();
        }

        // A finalizer, so a failing load still writes its line and leaves nothing listed.
        static Exception LoadFinalizer(Exception __exception)
        {
            load.Stop();
            LoadTimer.SaveLoaded();
            if (!loading) return __exception;
            loading = false;
            try
            {
                string checkText = Enabled
                    ? "item UI checks: " + checks.Asked + " asked, " + checks.Ran + " run, " + checks.Skipped + " skipped (gone)"
                    : "item UI checks: " + uncheckedAsked + " run as asked (option off)";
                UnityEngine.Debug.Log("[PerformanceFixes] save load: " + load.ElapsedMilliseconds + " ms, decoding " +
                    decode.ElapsedMilliseconds + " ms; " + checkText + (failed > 0 ? ", " + failed + " failed" : "") +
                    "; item UI nodes built in place: " + (ItemNodes.Built - nodesAtStart) +
                    "; store preload: " + (StoreRead.LastNarrowed ? "upgrades only" : "whole store") +
                    "; shape searches skipped: " + (ShapeChecks.WalksSkipped - walksSkippedAtStart) + " of " + (ShapeChecks.Computed - shapesAtStart) +
                    "; items per inventory: " + string.Join(", ", counts.ConvertAll(n => n.ToString()).ToArray()));
            }
            catch (Exception) { }
            return __exception;
        }

        static void DecodePrefix()
        {
            decode.Start();
            if (Enabled) checks.Enter();
        }

        static Exception DecodeFinalizer(GameInventory gameInventory, Exception __exception)
        {
            try
            {
                if (checks.Leave())
                    checks.Flush(e => e.Validate(), e => !e.IsDestroyed(), Failed);
            }
            finally
            {
                decode.Stop();
                if (loading)
                    try { counts.Add(gameInventory == null ? -1 : gameInventory.childItems.Count); } catch (Exception) { counts.Add(-1); }
            }
            return __exception;
        }

        static bool ValidatePrefix(GameItemElement __instance)
        {
            if (!Enabled && !checks.Flushing)
            {
                if (loading && decode.IsRunning) uncheckedAsked++;
                return true;
            }
            return checks.Request(__instance);
        }

        static void Failed(GameItemElement element, Exception e)
        {
            if (failed++ == 0)
                UnityEngine.Debug.LogWarning("[PerformanceFixes] an item UI check in the combined pass failed (later failures are only counted): " + e);
        }
    }
}
