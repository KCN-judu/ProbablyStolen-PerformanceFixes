using System;
using System.Reflection;
using HarmonyLib;

namespace PerformanceFixes
{
    // Fix 13: the early look at a save's store reads only what it looks at.
    //
    // Almost all of a save is its first key, "playerStore" (13 MB for a large store, nearly all of it the inventories'
    // JSON). Before the save loads, PlayerStore.PreLoadStore read the whole key into a new PlayerStore, and
    // EmporiumEntry.Start only looked at four of its network upgrades (whether the display case and the fixtures are
    // upgraded) and dropped it; PlayerStore.LoadGame then reads the whole key again, into the real store.
    //
    // Now, while PreLoadStore runs, Easy Save's description of PlayerStore lists only the network upgrades, so Easy
    // Save's own reader reads them exactly as before and passes over everything else, as it does for any key it does
    // not know. The PlayerStore is still created as before, and nothing reads anything else of it. The real load is
    // untouched.
    static class StoreRead
    {
        public static bool Enabled = true;
        // Number of preloads narrowed; LastNarrowed is for the save-load log line.
        public static int Preloads;
        public static bool LastNarrowed;   // whether the last preload read only the upgrades

        const string UpgradesMember = "networkUpgrade";

        // ES3Type.members is the list of members Easy Save reads for a type; it is not public, hence reflection. While
        // narrowed is set, the type holds the short list and allMembers the full one, which Restore puts back.
        static FieldInfo membersField;
        static ES3Types.ES3Type narrowed;
        static ES3Internal.ES3Member[] allMembers;

        // Prefix narrows the description, finalizer restores it, so the real load always sees the whole type.
        public static void Patch(Harmony harmony)
        {
            membersField = typeof(ES3Types.ES3Type).GetField("members", PerformanceMod.AnyInstance);
            var preload = typeof(PlayerStore).GetMethod("PreLoadStore", PerformanceMod.AnyStatic, null, Type.EmptyTypes, null);
            if (membersField == null || preload == null) throw new MissingMemberException("ES3Type.members / PlayerStore.PreLoadStore");
            harmony.Patch(preload, new HarmonyMethod(typeof(StoreRead).GetMethod("PreloadPrefix", PerformanceMod.AnyStatic)), null, null,
                new HarmonyMethod(typeof(StoreRead).GetMethod("PreloadFinalizer", PerformanceMod.AnyStatic)));
        }

        // On any failure the description is restored and the preload reads everything, as the game does.
        static void PreloadPrefix()
        {
            LastNarrowed = false;
            if (!Enabled) return;
            try
            {
                var type = ES3Internal.ES3TypeMgr.GetOrCreateES3Type(typeof(PlayerStore), true);
                var members = membersField.GetValue(type) as ES3Internal.ES3Member[];
                if (members == null) return;
                foreach (var m in members)
                {
                    if (m == null || m.name != UpgradesMember) continue;
                    allMembers = members;
                    narrowed = type;
                    membersField.SetValue(type, new[] { m });
                    Preloads++;
                    LastNarrowed = true;
                    return;
                }
            }
            catch (Exception e) { Restore(); UnityEngine.Debug.LogWarning("[PerformanceFixes] the store preload reads everything this time: " + e.Message); }
        }

        // A finalizer, so Easy Save's description of PlayerStore is whole again even if the read throws.
        static Exception PreloadFinalizer(Exception __exception)
        {
            Restore();
            return __exception;
        }

        // Puts the full member list back; safe to call when nothing is narrowed.
        static void Restore()
        {
            if (narrowed == null) return;
            var type = narrowed;
            narrowed = null;
            membersField.SetValue(type, allMembers);
            allMembers = null;
        }
    }
}
