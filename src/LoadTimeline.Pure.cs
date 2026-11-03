using System.Collections.Generic;
using System.Text;

namespace PerformanceFixes
{
    // The steps of one trip from the main menu into the store, with the time of each, for one line in Player.log.
    // Uses only System types, so tests/LoadTimelineTest.cs runs it outside the game.
    //
    // Marks are times in milliseconds since the button was pressed, in any order of arrival; the report lists them in
    // time order with the gap since the step before, so the gaps always add up to the total. A step marked twice keeps
    // its first time.
    sealed class LoadTimeline
    {
        readonly List<string> names = new List<string>();
        readonly List<double> times = new List<double>();

        public bool Started { get; private set; }

        public void Begin()
        {
            names.Clear();
            times.Clear();
            Started = true;
            Mark("button", 0);
        }

        public void Mark(string step, double ms)
        {
            if (!Started || names.Contains(step)) return;
            names.Add(step);
            times.Add(ms);
        }

        public bool Has(string step) { return names.Contains(step); }

        public void End() { Started = false; }

        public string Report()
        {
            var order = new List<int>();
            for (int i = 0; i < names.Count; i++) order.Add(i);
            order.Sort((a, b) => times[a] != times[b] ? times[a].CompareTo(times[b]) : a.CompareTo(b));
            var text = new StringBuilder();
            double previous = 0, last = 0;
            foreach (var i in order)
            {
                if (i == order[0] && names[i] == "button") { previous = times[i]; continue; }
                if (text.Length > 0) text.Append(", ");
                text.Append(names[i]).Append(" +").Append((times[i] - previous).ToString("0")).Append(" ms");
                previous = times[i];
                last = times[i];
            }
            return "total " + last.ToString("0") + " ms: " + text;
        }
    }
}
