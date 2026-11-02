// Checks the customer's buying rule over contents (src/BuyerRule.Pure.cs), outside the game.
//   tests/run.sh, or: csc -out:BuyerRuleTest.exe tests\BuyerRuleTest.cs src\BuyerRule.Pure.cs && BuyerRuleTest.exe
using System;
using System.Collections.Generic;
using PerformanceFixes;

static class BuyerRuleTest
{
    sealed class Node
    {
        public bool item = true, acceptable = true, contentsChecked = true;
        public readonly List<Node> children = new List<Node>();
        public Node Add(params Node[] nodes) { children.AddRange(nodes); return this; }
    }

    static int failed, acceptableCalls;

    static readonly BuyerRule<Node> rule = new BuyerRule<Node>(
        n => n.item, n => { acceptableCalls++; return n.acceptable; }, n => n.contentsChecked, n => n.children);

    static Node Item() { return new Node(); }
    static Node Refused() { return new Node { acceptable = false }; }
    static Node Unchecked() { return new Node { contentsChecked = false }; }
    static Node Inventory() { return new Node { item = false }; }

    static string Show(bool? b) { return b == null ? "no answer" : b.Value.ToString(); }

    static void Check(string what, bool? expected, bool? got)
    {
        if (got != expected) failed++;
        Console.WriteLine((got == expected ? "ok    " : "FAIL  ") + what + (got == expected ? "" : " (got " + Show(got) + ")"));
    }

    // The game's rule as it reads, without a depth limit: an item is bought when it is acceptable and, if its
    // contents are checked, every item anywhere below it is bought.
    static bool Reference(Node x) { return x.acceptable && (!x.contentsChecked || EverythingBelow(x)); }

    static bool EverythingBelow(Node x)
    {
        foreach (var c in x.children)
        {
            if (c.item && !Reference(c)) return false;
            if (!EverythingBelow(c)) return false;
        }
        return true;
    }

    // A chain of depth containers, the innermost holding last.
    static Node Chain(int depth, Node last, bool throughInventories)
    {
        Node node = last;
        for (int i = 0; i < depth; i++) node = throughInventories ? Inventory().Add(node) : Item().Add(node);
        return throughInventories ? Item().Add(node) : node;
    }

    static int Depth(Node x)
    {
        int d = 0;
        foreach (var c in x.children) d = Math.Max(d, 1 + Depth(c));
        return d;
    }

    static Node RandomTree(Random random, int depth)
    {
        var node = random.Next(4) == 0 && depth > 0 ? Inventory() : Item();
        node.acceptable = random.Next(8) != 0;
        node.contentsChecked = random.Next(3) != 0;
        if (depth > 0)
            for (int i = random.Next(4); i > 0; i--) node.children.Add(RandomTree(random, depth - 1));
        return node;
    }

    static int Main()
    {
        Check("an acceptable item on its own is bought", true, rule.Buying(Item()));
        Check("an unacceptable item is not bought", false, rule.Buying(Refused()));
        Check("a checked container with a refused item inside is not bought", false, rule.Buying(Item().Add(Item(), Refused())));
        Check("an unchecked container ignores what is inside", true, rule.Buying(Unchecked().Add(Refused())));
        Check("a refused item behind an inventory counts", false, rule.Buying(Item().Add(Inventory().Add(Refused()))));
        Check("a refused item inside an unchecked item inside a checked container counts", false,
            rule.Buying(Item().Add(Unchecked().Add(Refused()))));
        Check("an empty inventory node passes", true, rule.Buying(Item().Add(Inventory())));

        int limit = BuyerRule<Node>.MaxDepth;
        Check("a refused item 10 levels down is found", false, rule.Buying(Chain(10, Refused(), false)));
        Check("acceptable items 10 levels down are bought", true, rule.Buying(Chain(10, Item(), false)));
        Check("a refused item just inside the depth limit is found", false, rule.Buying(Chain(limit, Refused(), false)));
        // The depth limit never passes an item unchecked: deeper contents give no answer, and the caller asks the game.
        Check("a refused item past the depth limit gives no answer", null, rule.Buying(Chain(limit + 8, Refused(), false)));
        Check("acceptable items past the depth limit give no answer", null, rule.Buying(Chain(limit + 8, Item(), false)));
        Check("inventories past the depth limit give no answer", null, rule.Buying(Chain(limit + 8, Refused(), true)));
        Check("a refused item next to deep contents is never bought", true,
            rule.Buying(Item().Add(Chain(limit + 8, Item(), false), Refused())) != true &&
            rule.Buying(Item().Add(Refused(), Chain(limit + 8, Item(), false))) != true);
        Check("the rule answers again after a call that went too deep", false, rule.Buying(Item().Add(Refused())));

        Check("refusing on its own refuses without looking inside", true, rule.Refusing(Item().Add(Chain(limit + 8, Item(), false)), true));
        Check("a refused item inside a checked container refuses it", true, rule.Refusing(Item().Add(Refused()), false));
        Check("a refused item inside an unchecked container does not", false, rule.Refusing(Unchecked().Add(Refused()), false));
        Check("acceptable contents do not refuse", false, rule.Refusing(Item().Add(Item(), Item()), false));
        Check("refusal past the depth limit gives no answer", null, rule.Refusing(Chain(limit + 8, Refused(), false), false));

        // Each item is asked once per call, however often it is reached.
        var shared = Item();
        var inventory = Inventory().Add(shared);
        var container = Item().Add(inventory, inventory, Item().Add(shared));
        acceptableCalls = 0;
        rule.Buying(container);
        Check("each item is asked once per call", true, acceptableCalls == 3);

        // Against the rule as it reads, on random stores: the same answer whenever the rule gives one, and an answer
        // whenever the store is within the depth limit.
        var random = new Random(12345);
        int same = 0, cases = 2000, mismatched = 0, missing = 0;
        for (int i = 0; i < cases; i++)
        {
            var tree = RandomTree(random, 1 + random.Next(i % 50 == 0 ? 40 : 8));
            var got = rule.Buying(tree);
            if (got == null) { if (Depth(tree) <= limit) missing++; continue; }
            if (got.Value == Reference(tree)) same++; else mismatched++;
        }
        Check("random stores: never a different answer from the rule as it reads", true, mismatched == 0);
        Check("random stores: an answer for every store within the depth limit", true, missing == 0);
        Check("random stores: most stores answered", true, same > cases * 9 / 10);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
