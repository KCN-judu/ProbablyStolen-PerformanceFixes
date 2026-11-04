using System.Collections.Generic;

namespace PerformanceFixes
{
    // Which patches a fix added, from the mod's patches before and after it installed. Uses only System types, so
    // tests/PatchRollbackTest.cs runs it outside the game. A fix that fails halfway removes exactly these, so it is
    // either fully on or fully off and the other fixes keep theirs.
    static class PatchRollback
    {
        // after minus before, without duplicates, in order of appearance.
        public static List<T> Added<T>(ICollection<T> before, IEnumerable<T> after)
        {
            var added = new List<T>();
            foreach (var patch in after)
                if (!before.Contains(patch) && !added.Contains(patch)) added.Add(patch);
            return added;
        }
    }
}
