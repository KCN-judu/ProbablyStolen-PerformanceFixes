// Checks when the buyer check may be installed (src/LocaleGate.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:LocaleGateTest.exe tests\LocaleGateTest.cs src\LocaleGate.Pure.cs && LocaleGateTest.exe
using System;
using PerformanceFixes;

static class LocaleGateTest
{
    static int failed;

    static void Check(string what, LocaleGate.State expected, LocaleGate.State got)
    {
        if (got != expected) failed++;
        Console.WriteLine((got == expected ? "ok    " : "FAIL  ") + what + (got == expected ? "" : " (got " + got + ")"));
    }

    static int Main()
    {
        const float timeout = 300f;
        Check("before the game resolves its language, wait", LocaleGate.State.Wait,
            LocaleGate.Decide(false, null, null, 1f, timeout));
        Check("while localization is still starting, wait", LocaleGate.State.Wait,
            LocaleGate.Decide(false, "zh", "en", 2f, timeout));
        // Mods start while the game is still on English: the case that must not install.
        Check("while English is still selected for a Chinese player, wait", LocaleGate.State.Wait,
            LocaleGate.Decide(true, "zh", "en", 3f, timeout));
        Check("once Chinese is selected, install", LocaleGate.State.Ready,
            LocaleGate.Decide(true, "zh", "zh", 4f, timeout));
        Check("an English player installs once English is applied", LocaleGate.State.Ready,
            LocaleGate.Decide(true, "en", "en", 4f, timeout));
        Check("Brazilian Portuguese by its game code", LocaleGate.State.Ready,
            LocaleGate.Decide(true, "ptBR", "ptBR", 4f, timeout));
        Check("a language never applied gives up after the time-out", LocaleGate.State.GiveUp,
            LocaleGate.Decide(true, "zh", "en", timeout, timeout));
        Check("a language never resolved gives up after the time-out", LocaleGate.State.GiveUp,
            LocaleGate.Decide(false, null, null, timeout + 1f, timeout));
        Check("a language applied late still installs", LocaleGate.State.Ready,
            LocaleGate.Decide(true, "ja", "ja", timeout + 1f, timeout));

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
