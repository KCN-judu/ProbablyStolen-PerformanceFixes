// Checks that the item UI checks of a save-load part become one pass, each item once (src/CombinedPass.Pure.cs), outside
// the game.
//   tests/run.sh, or: csc -out:CombinedPassTest.exe tests\CombinedPassTest.cs src\CombinedPass.Pure.cs && CombinedPassTest.exe
using System;
using System.Collections.Generic;
using PerformanceFixes;

static class CombinedPassTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    // An item whose check, like the game's, also asks for a check of every item it is inside.
    sealed class Item
    {
        public string name;
        public Item parent;
        public bool gone;
        public int checkedTimes;
        public override string ToString() { return name; }
    }

    static CombinedPass<Item> checks;
    static List<string> order;

    // What the patched method does: ask first, and run the real check only when allowed.
    static void Validate(Item item)
    {
        if (!checks.Request(item)) return;
        item.checkedTimes++;
        order.Add(item.name);
        if (item.parent != null) Validate(item.parent);
    }

    static void Flush()
    {
        checks.Flush(Validate, i => !i.gone, (i, e) => { throw e; });
    }

    static int Main()
    {
        checks = new CombinedPass<Item>();
        order = new List<string>();
        var shelf = new Item { name = "shelf" };
        var crate = new Item { name = "crate", parent = shelf };
        var bottle = new Item { name = "bottle", parent = crate };
        var can = new Item { name = "can", parent = crate };

        Validate(bottle);
        Check("outside a load a check runs at once, with the items around it", order.Count == 3 && bottle.checkedTimes == 1);

        order.Clear();
        shelf.checkedTimes = crate.checkedTimes = bottle.checkedTimes = 0;
        checks.Enter();
        for (int i = 0; i < 40; i++) { Validate(bottle); Validate(can); Validate(crate); }
        Check("inside a load nothing runs yet", order.Count == 0);
        Check("each item is listed once however often it is asked", checks.Listed == 3);
        Check("every request is counted", checks.Asked == 120);

        checks.Enter();
        Validate(bottle);
        Check("an inner part does not run the checks", !checks.Leave() && order.Count == 0);
        Check("the outer part does", checks.Leave());
        Flush();
        Check("each item is checked exactly once", bottle.checkedTimes == 1 && can.checkedTimes == 1 && crate.checkedTimes == 1 && shelf.checkedTimes == 1);
        Check("in the order first asked, with containers pulled in once", string.Join(",", order.ToArray()) == "bottle,can,crate,shelf");
        Check("four checks ran instead of a hundred and twenty and more", checks.Ran == 4);
        Check("nothing is listed afterwards", checks.Listed == 0 && !checks.Flushing);

        order.Clear();
        checks.ResetStatistics();
        var temp = new Item { name = "temp" };
        checks.Enter();
        Validate(temp); Validate(bottle);
        temp.gone = true;
        checks.Leave();
        Flush();
        Check("an item destroyed before the end is skipped", temp.checkedTimes == 0 && checks.Skipped == 1);
        Check("the others are still checked", bottle.checkedTimes == 2);

        Validate(bottle);
        Check("after the load, checks run at once again", bottle.checkedTimes == 3);

        Check("leaving without entering is harmless", !checks.Leave());

        checks.Enter();
        Validate(bottle); Validate(can);
        checks.Leave();
        int ran = 0;
        checks.Flush(i => { if (i == bottle) throw new InvalidOperationException(); ran++; checks.Request(i); },
            i => true, (i, e) => { });
        Check("a failing check does not stop the others", ran == 1 && checks.Listed == 0 && !checks.Flushing);

        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
