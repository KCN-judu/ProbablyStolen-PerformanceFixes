using System.Collections.Generic;

namespace PerformanceFixes
{
    // When a requested layout runs, and which ones the end of the frame still owes. Uses only System types, so
    // tests/LayoutBatchTest.cs runs it outside the game.
    //
    // Inside a batching scope (Enter / Leave, which may nest) a request is only recorded, once per target. Outside,
    // it runs at once and strikes the target from the record, since it is now laid out. At the end of the frame
    // Take hands over the record; requests made while those are being laid out run at once.
    sealed class LayoutBatch<T> where T : class
    {
        // A set: a second request for the same inventory in one frame costs nothing.
        readonly HashSet<T> pending = new HashSet<T>();
        int depth;

        // True between Take and Done; requests then run at once instead of being recorded again.
        public bool Flushing { get; private set; }
        public int Count { get { return pending.Count; } }

        // Scopes nest because ValidateShapeState can call itself through other items.
        public void Enter() { depth++; }
        public void Leave() { if (depth > 0) depth--; }

        // Whether a layout of target asked for now should run now. newlyDeferred tells a first deferral of target.
        public bool Request(T target, out bool newlyDeferred)
        {
            newlyDeferred = false;
            if (depth > 0 && !Flushing)
            {
                newlyDeferred = pending.Add(target);
                return false;
            }
            if (!Flushing) pending.Remove(target);
            return true;
        }

        // Starts the end-of-frame layout: moves the record into batch, or answers false when there is nothing to do
        // or a layout is already under way.
        public bool Take(List<T> batch)
        {
            if (pending.Count == 0 || Flushing) return false;
            Flushing = true;
            batch.Clear();
            batch.AddRange(pending);
            pending.Clear();
            return true;
        }

        // Ends the end-of-frame layout started by Take.
        public void Done() { Flushing = false; }

        // Drops every waiting layout without running it: their inventories are gone.
        public void Discard() { if (!Flushing) pending.Clear(); }
    }
}
