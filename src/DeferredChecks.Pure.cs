using System;
using System.Collections.Generic;

namespace PerformanceFixes
{
    // Which UI checks wait for the end of a batching scope, and running each of them once. Uses only System types, so
    // tests/DeferredChecksTest.cs runs it outside the game.
    //
    // Inside a scope (Enter / Leave, which may nest), a check is only recorded, once per target, in the order first
    // asked. When the outermost scope ends, Flush runs the recorded checks in that order, each target once. A check
    // asked for while they run is not run again for a target already checked or still waiting; a new target is added
    // to the end and checked in turn. A target that is gone by then (alive answers false) is skipped.
    sealed class DeferredChecks<T> where T : class
    {
        readonly List<T> waiting = new List<T>();
        readonly HashSet<T> recorded = new HashSet<T>();
        T running;
        int depth;

        public bool Flushing { get; private set; }
        public int Waiting { get { return waiting.Count; } }

        // Statistics: checks asked for inside scopes, and checks actually run by Flush.
        public int Asked, Ran, Skipped;

        public void Enter() { depth++; }

        // Ends a scope; answers true when it was the outermost, so the caller should Flush.
        public bool Leave()
        {
            if (depth == 0) return false;
            return --depth == 0;
        }

        // Whether a check of target asked for now should run now.
        public bool Request(T target)
        {
            if (Flushing)
            {
                if (target == running) { running = null; return true; }
                if (recorded.Add(target)) waiting.Add(target);
                return false;
            }
            if (depth == 0) return true;
            Asked++;
            if (recorded.Add(target)) waiting.Add(target);
            return false;
        }

        // Runs every recorded check once. check runs the real check (which asks Request again and is let through);
        // a failure of one check is passed to failed and does not stop the others.
        public void Flush(Action<T> check, Func<T, bool> alive, Action<T, Exception> failed)
        {
            if (Flushing || waiting.Count == 0) return;
            Flushing = true;
            try
            {
                for (int i = 0; i < waiting.Count; i++)
                {
                    var target = waiting[i];
                    if (!alive(target)) { Skipped++; continue; }
                    running = target;
                    try { check(target); Ran++; }
                    catch (Exception e) { failed(target, e); }
                    finally { running = null; }
                }
            }
            finally
            {
                waiting.Clear();
                recorded.Clear();
                Flushing = false;
            }
        }

        public void ResetStatistics() { Asked = Ran = Skipped = 0; }
    }
}
