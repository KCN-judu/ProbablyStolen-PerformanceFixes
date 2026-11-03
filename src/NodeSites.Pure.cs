using System.Collections.Generic;

namespace PerformanceFixes
{
    // Finds where a method creates a UI node and then moves it under a parent of its own, so the two steps can be one.
    // Works on the method's instructions reduced to the kinds below. Uses only System types, so tests/NodeSitesTest.cs
    // runs it outside the game.
    //
    // A site is exactly four instructions in a row:
    //   create a node (or a text node)        the game puts it in a shared temporary parent
    //   load this
    //   call a getter with no arguments, or   the node's real parent
    //   read one of this object's node fields
    //   move the node under that parent
    // Anything else in between, and the site does not count.
    static class NodeSites
    {
        public enum Kind { Other, CreateNode, CreateText, LoadThis, Getter, ParentField, MoveUnder }

        public struct Site
        {
            public int Index;     // of the create instruction
            public bool Text;
        }

        public static List<Site> Find(IList<Kind> code)
        {
            var sites = new List<Site>();
            for (int i = 0; i + 3 < code.Count; i++)
            {
                var k = code[i];
                if (k != Kind.CreateNode && k != Kind.CreateText) continue;
                if (code[i + 1] != Kind.LoadThis || (code[i + 2] != Kind.Getter && code[i + 2] != Kind.ParentField) || code[i + 3] != Kind.MoveUnder) continue;
                sites.Add(new Site { Index = i, Text = k == Kind.CreateText });
                i += 3;
            }
            return sites;
        }
    }
}
