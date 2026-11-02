// Checks which highlight squares are switched off after a refresh (src/HighlightDiff.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:HighlightDiffTest.exe tests\HighlightDiffTest.cs src\HighlightDiff.Pure.cs && HighlightDiffTest.exe
using System;
using System.Collections.Generic;
using PerformanceFixes;

static class HighlightDiffTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static HashSet<int> Stale(int[] before, int[] now)
    {
        var previous = new HashSet<int>(before);
        HighlightDiff.KeepStale(previous, new List<int>(now));
        return previous;
    }

    static int Main()
    {
        Check("an unchanged highlight switches nothing off", Stale(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }).Count == 0);
        Check("a moved highlight switches off only what it left", Stale(new[] { 1, 2, 3 }, new[] { 2, 3, 4 }).SetEquals(new[] { 1 }));
        Check("a cleared highlight switches everything off", Stale(new[] { 1, 2 }, new int[0]).SetEquals(new[] { 1, 2 }));
        Check("a first highlight switches nothing off", Stale(new int[0], new[] { 5, 6 }).Count == 0);
        Check("a square lit twice in one refresh stays on", Stale(new[] { 1, 2 }, new[] { 2, 2 }).SetEquals(new[] { 1 }));

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
