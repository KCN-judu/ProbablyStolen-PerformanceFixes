// Checks when the group-drag fit check must run again (src/FormationFit.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:FormationFitTest.exe tests\FormationFitTest.cs src\FormationFit.Pure.cs && FormationFitTest.exe
using System;
using System.Collections.Generic;
using PerformanceFixes;

static class FormationFitTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static int Main()
    {
        var fit = new FormationFit<string, string>();
        var items = new List<string> { "crate", "barrel" };
        bool fits;

        Check("nothing is known at first", !fit.TryGet("bay", 1, 2, 7, items, out fits));
        fit.Store("bay", 1, 2, 7, items, true);
        Check("the same drop gives the stored answer", fit.TryGet("bay", 1, 2, 7, items, out fits) && fits);
        fit.Store("bay", 1, 2, 7, items, false);
        Check("a stored refusal is given back", fit.TryGet("bay", 1, 2, 7, items, out fits) && !fits);

        Check("another inventory checks again", !fit.TryGet("shelf", 1, 2, 7, items, out fits));
        Check("another column checks again", !fit.TryGet("bay", 0, 2, 7, items, out fits));
        Check("another row checks again", !fit.TryGet("bay", 1, 3, 7, items, out fits));
        Check("any change in an inventory checks again", !fit.TryGet("bay", 1, 2, 8, items, out fits));
        Check("a smaller selection checks again", !fit.TryGet("bay", 1, 2, 7, new List<string> { "crate" }, out fits));
        Check("the same items in another order check again", !fit.TryGet("bay", 1, 2, 7, new List<string> { "barrel", "crate" }, out fits));

        items.Add("box");
        Check("a selection changed after storing checks again", !fit.TryGet("bay", 1, 2, 7, items, out fits));

        fit.Store("bay", 1, 2, 7, items, true);
        fit.Forget();
        Check("after the selection is cleared nothing is known", !fit.TryGet("bay", 1, 2, 7, items, out fits));

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
