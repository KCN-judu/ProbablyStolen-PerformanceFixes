// Checks when a requested inventory layout runs (src/LayoutBatch.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:LayoutBatchTest.exe tests\LayoutBatchTest.cs src\LayoutBatch.Pure.cs && LayoutBatchTest.exe
using System;
using System.Collections.Generic;
using PerformanceFixes;

static class LayoutBatchTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static int Main()
    {
        bool fresh;
        var batch = new LayoutBatch<string>();
        var taken = new List<string>();

        Check("outside a batching scope a layout runs at once", batch.Request("bay", out fresh) && !fresh);
        Check("with nothing waiting there is nothing to take", !batch.Take(taken));

        batch.Enter();
        Check("inside a scope a layout waits", !batch.Request("bay", out fresh) && fresh);
        Check("asking again for the same inventory waits without counting twice", !batch.Request("bay", out fresh) && !fresh);
        batch.Request("shelf", out fresh);
        batch.Enter();
        Check("nested scopes still wait", !batch.Request("crate", out fresh) && fresh);
        batch.Leave();
        Check("leaving the inner scope still waits", !batch.Request("crate", out fresh) && !fresh);
        batch.Leave();
        Check("three inventories wait", batch.Count == 3);

        Check("after the scopes a layout runs at once", batch.Request("shelf", out fresh));
        Check("and that inventory no longer waits", batch.Count == 2);

        Check("the end of the frame takes the rest", batch.Take(taken) && taken.Count == 2 && batch.Count == 0);
        batch.Enter();
        Check("while they are laid out, a layout runs at once", batch.Flushing && batch.Request("bay", out fresh) && !fresh);
        Check("and nothing is added to the next batch", batch.Count == 0);
        Check("a second take while laying out does nothing", !batch.Take(taken));
        batch.Leave();
        batch.Done();
        Check("after the frame, waiting starts again", !batch.Flushing);

        batch.Leave();
        batch.Leave();
        Check("leaving more scopes than entered is harmless", batch.Request("bay", out fresh));

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
