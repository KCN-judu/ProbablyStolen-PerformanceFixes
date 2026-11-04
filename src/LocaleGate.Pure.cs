namespace PerformanceFixes
{
    // Whether a patch that must not run before the player's language is applied may be installed now. Uses only
    // System types, so tests/LocaleGateTest.cs runs it outside the game.
    //
    // Ready once localization has finished starting and the selected language is the one the game resolved at
    // startup. Until then, wait; past the time-out, give up, and the patch stays off for the session.
    static class LocaleGate
    {
        public enum State { Wait, Ready, GiveUp }

        // waited and timeout are in seconds. Wait is the answer until the language matches or the time is up.
        public static State Decide(bool initialized, string resolved, string selected, float waited, float timeout)
        {
            if (initialized && resolved != null && selected == resolved) return State.Ready;
            return waited >= timeout ? State.GiveUp : State.Wait;
        }
    }
}
