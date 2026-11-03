using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PerformanceFixes
{
    // Fix 15: entering the store rewrites the settings file only for settings that changed.
    //
    // EmporiumEntry.Start ends with EmporiumOption.ApplySetting, which writes each of the game's settings with
    // ES3.Save (33 of them) and then every key binding through KeyBindHelper.Save, one ES3.Save each. Each ES3.Save
    // reads the whole settings file (SaveFile.es3), merges in the one value, writes a temporary file and swaps it in:
    // about 57 complete rewrites of the same file on every load, nearly always with the values just read from it (about
    // half a second measured).
    //
    // While ApplySetting runs, each of those writes first reads the key back from the file, with the same settings
    // ES3.Save uses; when the file already holds that key with an equal value, the write is left out, because it would
    // give the same file. A key the file does not have, a different value, or anything that cannot be read back is
    // written exactly as the game writes it. Every other save, and KeyBindHelper.Save called on its own (rebinding a key
    // in the options), writes as before.
    static class SettingsWrites
    {
        public static bool Enabled = true;
        public static int Written, Unchanged;

        const int ApplySettingWrites = 33, KeyBindingWrites = 1;

        static int depth;
        static MethodInfo gameSave, checkedSave;

        public static void Patch(Harmony harmony)
        {
            foreach (var m in typeof(ES3).GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (m.Name != "Save" || !m.IsGenericMethodDefinition) continue;
                var p = m.GetParameters();
                if (p.Length == 2 && p[0].ParameterType == typeof(string)) gameSave = m;
            }
            checkedSave = typeof(SettingsWrites).GetMethod("Save", PerformanceMod.AnyStatic);
            if (gameSave == null || checkedSave == null) throw new MissingMethodException("ES3.Save<T>(string, T)");
            var apply = typeof(EmporiumOption).GetMethod("ApplySetting", PerformanceMod.AnyInstance, null, new[] { typeof(bool) }, null);
            var keys = typeof(KeyBindHelper).GetMethod("Save", PerformanceMod.AnyStatic, null, Type.EmptyTypes, null);
            if (apply == null || keys == null) throw new MissingMethodException("EmporiumOption.ApplySetting / KeyBindHelper.Save");
            harmony.Patch(apply, new HarmonyMethod(typeof(SettingsWrites).GetMethod("Begin", PerformanceMod.AnyStatic)), null,
                new HarmonyMethod(typeof(SettingsWrites).GetMethod("ApplyTranspiler", PerformanceMod.AnyStatic)),
                new HarmonyMethod(typeof(SettingsWrites).GetMethod("End", PerformanceMod.AnyStatic)));
            harmony.Patch(keys, null, null, new HarmonyMethod(typeof(SettingsWrites).GetMethod("KeysTranspiler", PerformanceMod.AnyStatic)));
        }

        static IEnumerable<CodeInstruction> ApplyTranspiler(IEnumerable<CodeInstruction> instructions) { return Redirect(instructions, ApplySettingWrites); }
        static IEnumerable<CodeInstruction> KeysTranspiler(IEnumerable<CodeInstruction> instructions) { return Redirect(instructions, KeyBindingWrites); }

        // Each call of ES3.Save<T>(key, value) calls SettingsWrites.Save<T>(key, value) instead; anything else, or a
        // different number of such calls than checked against the game, leaves the method as it is.
        static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions, int expected)
        {
            var code = new List<CodeInstruction>(instructions);
            int found = 0;
            foreach (var i in code)
            {
                var called = i.operand as MethodInfo;
                if (called == null || !called.IsGenericMethod || called.GetGenericMethodDefinition() != gameSave) continue;
                i.operand = checkedSave.MakeGenericMethod(called.GetGenericArguments());
                found++;
            }
            if (found != expected) throw new InvalidOperationException("expected " + expected + " settings writes, found " + found);
            return code;
        }

        static int writtenAtStart, unchangedAtStart;

        static void Begin()
        {
            if (depth++ > 0) return;
            writtenAtStart = Written;
            unchangedAtStart = Unchanged;
        }

        // A finalizer, so a failing ApplySetting still ends the check.
        static Exception End(Exception __exception)
        {
            if (depth == 0 || --depth > 0) return __exception;
            if (Unchanged > unchangedAtStart)
                UnityEngine.Debug.Log("[PerformanceFixes] settings applied: " + (Written - writtenAtStart) + " written, " +
                    (Unchanged - unchangedAtStart) + " already in the file and left out");
            return __exception;
        }

        // The game's ES3.Save<T>(key, value), left out while ApplySetting runs when the file already holds that value.
        static void Save<T>(string key, T value)
        {
            if (Enabled && depth > 0 && Holds(key, value)) { Unchanged++; return; }
            ES3.Save(key, value);
            Written++;
        }

        static bool Holds<T>(string key, T value)
        {
            try { return EqualityComparer<T>.Default.Equals(ES3.Load<T>(key), value); }
            catch (Exception) { return false; }   // no such key, no file, or unreadable: written as the game writes it
        }
    }
}
