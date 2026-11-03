// Checks which UI-node creations are fused with their move under a parent (src/NodeSites.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:NodeSitesTest.exe tests\NodeSitesTest.cs src\NodeSites.Pure.cs && NodeSitesTest.exe
using System;
using System.Collections.Generic;
using PerformanceFixes;
using K = PerformanceFixes.NodeSites.Kind;

static class NodeSitesTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static List<NodeSites.Site> Find(params K[] code) { return NodeSites.Find(code); }

    static int Main()
    {
        var one = Find(K.CreateNode, K.LoadThis, K.Getter, K.MoveUnder);
        Check("a node created and moved under a getter's parent is a site", one.Count == 1 && one[0].Index == 0 && !one[0].Text);

        var text = Find(K.Other, K.CreateText, K.LoadThis, K.Getter, K.MoveUnder, K.Other);
        Check("so is a text node, at its own position", text.Count == 1 && text[0].Index == 1 && text[0].Text);

        Check("a node kept where it was made is not", Find(K.CreateNode, K.Other, K.Other, K.Other).Count == 0);
        Check("a parent from somewhere else than this is not", Find(K.CreateNode, K.Other, K.Getter, K.MoveUnder).Count == 0);
        Check("a parent computed with more steps is not", Find(K.CreateNode, K.LoadThis, K.Getter, K.Getter, K.MoveUnder).Count == 0);
        Check("a node stored before its move is not", Find(K.CreateNode, K.Other, K.LoadThis, K.Getter, K.MoveUnder).Count == 0);
        Check("a site cut off at the end is not", Find(K.CreateNode, K.LoadThis, K.Getter).Count == 0);

        var many = new List<K>();
        for (int i = 0; i < 12; i++)
        {
            many.Add(i % 3 == 0 ? K.CreateText : K.CreateNode);
            many.AddRange(new[] { K.LoadThis, K.Getter, K.MoveUnder, K.Other });
        }
        var sites = NodeSites.Find(many);
        Check("twelve sites in a row are all found", sites.Count == 12);
        Check("in order, at the right places", sites[0].Index == 0 && sites[11].Index == 55);
        Check("text nodes are told apart", sites.FindAll(s => s.Text).Count == 4);

        Check("back-to-back sites are both found",
            Find(K.CreateNode, K.LoadThis, K.Getter, K.MoveUnder, K.CreateText, K.LoadThis, K.Getter, K.MoveUnder).Count == 2);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
