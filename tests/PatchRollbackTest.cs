// Checks which patches a failed fix removes again (src/PatchRollback.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:PatchRollbackTest.exe tests\PatchRollbackTest.cs src\PatchRollback.Pure.cs && PatchRollbackTest.exe
using System;
using System.Collections.Generic;
using System.Linq;
using PerformanceFixes;

static class PatchRollbackTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static KeyValuePair<string, string> P(string method, string patch) { return new KeyValuePair<string, string>(method, patch); }

    static int Main()
    {
        // Two fixes patch the same method; the second fails after one of its two patches.
        var before = new HashSet<KeyValuePair<string, string>> { P("ValidateShapeState", "Enter"), P("Validate", "Defer") };
        var after = new[] { P("ValidateShapeState", "Enter"), P("Validate", "Defer"), P("ValidateShapeState", "Count") };
        var added = PatchRollback.Added(before, after);
        Check("only the failed fix's patch is removed", added.Count == 1 && added[0].Equals(P("ValidateShapeState", "Count")));
        Check("the other fix's patch on the same method stays", !added.Contains(P("ValidateShapeState", "Enter")));

        Check("a fix that attached nothing removes nothing", PatchRollback.Added(before, before).Count == 0);
        Check("a fix that attached several removes all of them",
            PatchRollback.Added(new HashSet<KeyValuePair<string, string>>(), after).Count == 3);
        Check("a patch listed twice is removed once",
            PatchRollback.Added(before, after.Concat(new[] { P("ValidateShapeState", "Count") })).Count == 1);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
