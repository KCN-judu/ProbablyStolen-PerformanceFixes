using System.Collections.Generic;

namespace PerformanceFixes
{
    // Which highlight squares to switch off after a refresh. Uses only System types, so tests/HighlightDiffTest.cs
    // runs it outside the game.
    //
    // previous holds the squares lit before the refresh; lit, the squares lit by it. What stays in previous is lit
    // no more and is switched off; a square lit both times is left alone.
    static class HighlightDiff
    {
        public static void KeepStale<T>(HashSet<T> previous, List<T> lit)
        {
            foreach (var square in lit) previous.Remove(square);
        }
    }
}
