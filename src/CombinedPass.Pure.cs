using System;
using System.Collections.Generic;

namespace PerformanceFixes
{
    // Collects the repeated UI checks of one piece of work into a single pass over the targets. Uses only System types,
    // so tests/CombinedPassTest.cs runs it outside the game.
    //
    // Inside a scope (Enter / Leave, which may nest), each target is listed once, in the order first asked, however
    // often it is asked. When the outermost scope ends, Flush walks the list once and checks each target once. A target
    // asked for during that walk is not checked again if it is already checked or still listed; a new one (a container
    // around a checked item) is added to the end and checked in turn. A target that is gone (alive answers false) is
    // skipped.
    sealed class CombinedPass<T> where T : class
    {
        // listed keeps the order of first request; recorded makes the membership test constant-time.
        readonly List<T> listed = new List<T>();
        readonly HashSet<T> recorded = new HashSet<T>();
        // The target whose real check is running now: its own re-ask must be let through, not listed again.
        T running;
        int depth;

        // True while Flush walks the list; the game's checks that Flush triggers arrive here again.
        public bool Flushing { get; private set; }
        public int Listed { get { return listed.Count; } }

        // Statistics: checks asked for inside scopes, and checks the pass actually ran.
        public int Asked, Ran, Skipped;

        public void Enter() { depth++; }

        // Ends a scope; answers true when it was the outermost, so the caller should Flush.
        public bool Leave()
        {
            if (depth == 0) return false;
            return --depth == 0;
        }

        // Whether a check of target asked for now should run now, or is covered by the pass.
        public bool Request(T target)
        {
            if (Flushing)
            {
                if (target == running) { running = null; return true; }
                if (recorded.Add(target)) listed.Add(target);
                return false;
            }
            if (depth == 0) return true;
            Asked++;
            if (recorded.Add(target)) listed.Add(target);
            return false;
        }

        // The single pass: checks every listed target once. check runs the real check (which asks Request again and is let through);
        // a failure of one check is passed to failed and does not stop the others.
        public void Flush(Action<T> check, Func<T, bool> alive, Action<T, Exception> failed)
        {
            if (Flushing || listed.Count == 0) return;
            Flushing = true;
            try
            {
                for (int i = 0; i < listed.Count; i++)
                {
                    var target = listed[i];
                    if (!alive(target)) { Skipped++; continue; }
                    running = target;
                    try { check(target); Ran++; }
                    catch (Exception e) { failed(target, e); }
                    finally { running = null; }
                }
            }
            finally
            {
                listed.Clear();
                recorded.Clear();
                Flushing = false;
            }
        }

        public void ResetStatistics() { Asked = Ran = Skipped = 0; }
    }
}
