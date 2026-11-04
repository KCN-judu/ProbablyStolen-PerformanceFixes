using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PerformanceFixes
{
    // Fix 14: working out an item's shape and state skips the search under an item with nothing under it.
    //
    // GameItem.ComputeModifiedShapeState runs on every tag change and every move: about 58,000 times while a large
    // store loads, mostly while each item is being made. Each time it walks everything under the item for inventories
    // whose items change its shape or state, building a list, a set and a queue for the walk and a list for the
    // result. An item's children are its inventory window and its examine window (GameItem.children); an item with
    // neither has nothing under it, and the walk finds nothing.
    //
    // For such an item the walk is skipped and gives the same empty list. Everything else the computation does is
    // unchanged, and an item with anything under it is walked as before.
    static class ShapeChecks
    {
        public static bool Enabled = true;
        // Computations and skipped walks, for the save-load log line.
        public static int Computed, WalksSkipped;

        // Shared empty result; safe because the game's caller only enumerates it.
        static readonly List<GameInventory> none = new List<GameInventory>();
        // GraphUtils.FindChildrenType<T> (generic definition), the call the transpiler looks for.
        static MethodInfo walk;

        // A transpiler on ComputeModifiedShapeState, not a prefix: the rest of that method must still run as before.
        public static void Patch(Harmony harmony)
        {
            walk = typeof(GraphUtils).GetMethod("FindChildrenType", PerformanceMod.AnyStatic);
            var compute = typeof(GameItem).GetMethod("ComputeModifiedShapeState", PerformanceMod.AnyInstance);
            if (walk == null || compute == null) throw new MissingMethodException("GraphUtils.FindChildrenType / GameItem.ComputeModifiedShapeState");
            harmony.Patch(compute, null, null, new HarmonyMethod(typeof(ShapeChecks).GetMethod("Transpiler", PerformanceMod.AnyStatic)));
        }

        // The walk FindChildrenType<GameInventory>(item, null, null) calls Inventories instead. Anything else, or not
        // exactly one such walk, leaves the method as it is.
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int walks = 0;
            foreach (var i in code)
            {
                var called = i.operand as MethodInfo;
                if (called == null || !called.IsGenericMethod || called.GetGenericMethodDefinition() != walk
                    || called.GetGenericArguments()[0] != typeof(GameInventory)) continue;
                i.operand = typeof(ShapeChecks).GetMethod("Inventories", PerformanceMod.AnyStatic);
                walks++;
            }
            if (walks != 1) throw new InvalidOperationException("expected one walk, found " + walks);
            return code;
        }

        // What the walk finds under the node.
        // Same signature as the walk it replaces. A filter or passthrough means a different walk, which is always run.
        static List<GameInventory> Inventories(GraphNodeStorage node, Func<GameInventory, bool> filter, Func<GameInventory, bool> passthrough)
        {
            Computed++;
            var item = node as GameItem;
            if (Enabled && item != null && filter == null && passthrough == null
                && (object)item.child == null && (object)item.examineWindow == null)
            {
                WalksSkipped++;
                return none;   // only enumerated by the caller, never kept or changed
            }
            return GraphUtils.FindChildrenType(node, filter, passthrough);
        }
    }
}
