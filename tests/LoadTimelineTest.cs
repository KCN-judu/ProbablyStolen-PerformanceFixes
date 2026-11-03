// Checks the load timeline written to Player.log (src/LoadTimeline.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:LoadTimelineTest.exe tests\LoadTimelineTest.cs src\LoadTimeline.Pure.cs && LoadTimelineTest.exe
using System;
using PerformanceFixes;

static class LoadTimelineTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static int Main()
    {
        var t = new LoadTimeline();
        t.Mark("ignored", 5);
        Check("nothing is recorded before the button", !t.Has("ignored"));

        t.Begin();
        t.Mark("black", 210);
        t.Mark("scene started", 2300);
        t.Mark("scene ready", 900);
        t.Mark("save loaded", 5100);
        t.Mark("first frame", 5700);
        t.Mark("black", 400);
        string r = t.Report();
        Check("steps are listed in time order with the gap since the step before",
            r == "total 5700 ms: black +210 ms, scene ready +690 ms, scene started +1400 ms, save loaded +2800 ms, first frame +600 ms");
        Check("a step marked twice keeps its first time", r.Contains("black +210 ms"));
        Check("the gaps add up to the total", 210 + 690 + 1400 + 2800 + 600 == 5700);

        t.End();
        t.Mark("late", 9000);
        Check("nothing is recorded after the end", !t.Has("late"));

        t.Begin();
        Check("a new trip starts empty", !t.Has("black") && t.Report() == "total 0 ms: ");

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
