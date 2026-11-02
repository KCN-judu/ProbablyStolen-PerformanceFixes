using System;
using System.Collections.Generic;

namespace PerformanceFixes
{
    // The customer's buying rule over a container's contents, on any tree of nodes. Uses only System types, so
    // tests/BuyerRuleTest.cs runs it outside the game.
    //
    //   bought(x)          = acceptable(x) and (contents not checked(x) or everything inside x is bought)
    //   everything inside x = every item anywhere below x, through items and other nodes, each bought
    //   refusing(x)        = refusing on its own, or contents checked(x) and something inside is not bought
    //
    // Each item is evaluated once per call; answers are kept only until the next call. Below MaxDepth levels the
    // rule does not guess: the call answers null and the caller asks the game's own method instead.
    sealed class BuyerRule<T> where T : class
    {
        public const int MaxDepth = 32;

        readonly Func<T, bool> isItem, acceptable, contentsChecked;
        readonly Func<T, IList<T>> children;
        readonly Dictionary<T, bool> bought = new Dictionary<T, bool>();
        readonly Dictionary<T, bool> allInside = new Dictionary<T, bool>();
        readonly HashSet<T> nodesSeen = new HashSet<T>();
        bool tooDeep;

        public BuyerRule(Func<T, bool> isItem, Func<T, bool> acceptable, Func<T, bool> contentsChecked,
            Func<T, IList<T>> children)
        {
            this.isItem = isItem;
            this.acceptable = acceptable;
            this.contentsChecked = contentsChecked;
            this.children = children;
        }

        // bought(item), or null when the contents go deeper than MaxDepth.
        public bool? Buying(T item)
        {
            try { return Answer(Bought(item, 0)); }
            finally { Reset(); }
        }

        // refusing(item), or null when the contents go deeper than MaxDepth. The single-item answer comes first,
        // as in the game, and the contents are not looked at when it already refuses.
        public bool? Refusing(T item, bool refusingAlone)
        {
            try { return Answer(refusingAlone || (contentsChecked(item) && !AllInsideBought(item, 0))); }
            finally { Reset(); }
        }

        bool? Answer(bool result) { return tooDeep ? (bool?)null : result; }

        void Reset()
        {
            bought.Clear();
            allInside.Clear();
            nodesSeen.Clear();
            tooDeep = false;
        }

        bool Bought(T item, int depth)
        {
            bool result;
            if (bought.TryGetValue(item, out result)) return result;
            result = acceptable(item) && (!contentsChecked(item) || AllInsideBought(item, depth));
            bought[item] = result;
            return result;
        }

        bool AllInsideBought(T item, int depth)
        {
            bool result;
            if (allInside.TryGetValue(item, out result)) return result;
            if (depth >= MaxDepth) { tooDeep = true; return true; }
            result = NodesBought(children(item), depth + 1);
            allInside[item] = result;
            return result;
        }

        bool NodesBought(IList<T> nodes, int depth)
        {
            if (nodes == null) return true;
            for (int i = 0; i < nodes.Count && !tooDeep; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                if (isItem(node))
                {
                    if (!Bought(node, depth) || !AllInsideBought(node, depth)) return false;
                }
                else if (depth >= MaxDepth) tooDeep = true;
                else if (nodesSeen.Add(node) && !NodesBought(children(node), depth + 1)) return false;
            }
            return true;
        }
    }
}
