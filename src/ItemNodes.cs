using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PerformanceFixes
{
    // Fix 9: an item's UI nodes are created directly under their own parents instead of being created in a shared
    // temporary parent and then moved.
    //
    // The item UI constructor (GameItemElement..ctor) builds twelve nodes, besides the item's root node. Each one is
    // created with TreeNodeRender.Instantiate() or InstantiateText(), which put it in RenderHandler's temporary root,
    // and is then moved under one of the item's other nodes with SetParent. Moving a node in the UI hierarchy is the
    // expensive part: about 80 µs each inside the game's canvas, whatever the node holds, measured over a save load of
    // 1,342 items as most of 2.3 s.
    //
    // Each of those twelve create-then-move pairs is made one step: the node is created with InstantiateAsRoot under
    // its final parent, the font of a text node is applied as InstantiateText does, and the game's own SetParent then
    // runs as before, finding the node already in place (it still sets the node's parent and depth as usual). Every
    // parent is a node the constructor has just created and is active, so the new node starts up exactly as it would
    // in the temporary root; it ends with the same parent, the same position among its siblings and the same local
    // position, rotation and scale. Only these twelve sites change; the root node is created as before.
    //
    // The sites are found by NodeSites (its own file, with tests). If the constructor no longer has exactly twelve,
    // after a game update say, the fix does not install and Player.log says so.
    static class ItemNodes
    {
        public const int ExpectedSites = 12;

        public static bool Enabled = true;
        public static int Built;

        static MethodInfo applyLocaleFont;

        public static void Patch(Harmony harmony)
        {
            applyLocaleFont = typeof(TreeNodeRender).GetMethod("ApplyLocaleFont", PerformanceMod.AnyStatic, null, new[] { typeof(TreeNodeRender) }, null);
            if (applyLocaleFont == null) throw new MissingMethodException("TreeNodeRender.ApplyLocaleFont");
            var ctor = typeof(GameItemElement).GetConstructor(PerformanceMod.AnyInstance, null,
                new[] { typeof(string), typeof(string), typeof(string), typeof(PixelWindow) }, null);
            if (ctor == null) throw new MissingMethodException("GameItemElement..ctor");
            harmony.Patch(ctor, null, null, new HarmonyMethod(typeof(ItemNodes).GetMethod("Transpiler", PerformanceMod.AnyStatic)));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var node = typeof(TreeNodeRender).GetMethod("Instantiate", PerformanceMod.AnyStatic, null, Type.EmptyTypes, null);
            var text = typeof(TreeNodeRender).GetMethod("InstantiateText", PerformanceMod.AnyStatic, null, Type.EmptyTypes, null);
            var setParent = typeof(TreeNodeRender).GetMethod("SetParent", PerformanceMod.AnyInstance, null, new[] { typeof(TreeNodeRender) }, null);
            var nodeUnder = typeof(ItemNodes).GetMethod("NodeUnder", PerformanceMod.AnyStatic);
            var textUnder = typeof(ItemNodes).GetMethod("TextUnder", PerformanceMod.AnyStatic);
            if (node == null || text == null || setParent == null) throw new MissingMethodException("TreeNodeRender node methods");

            var code = new List<CodeInstruction>(instructions);
            var kinds = new List<NodeSites.Kind>(code.Count);
            foreach (var i in code) kinds.Add(KindOf(i, node, text, setParent));
            var sites = NodeSites.Find(kinds);
            if (sites.Count != ExpectedSites)
                throw new InvalidOperationException("expected " + ExpectedSites + " node sites in the item UI constructor, found " + sites.Count);

            // From the last site back, so earlier indices stay valid: drop the create, keep "this, getter", and turn the
            // move into the combined call, which leaves the node on the stack as the move did.
            for (int s = sites.Count - 1; s >= 0; s--)
            {
                int at = sites[s].Index;
                var create = code[at];
                code.RemoveAt(at);
                code[at].labels.AddRange(create.labels);
                code[at].blocks.AddRange(create.blocks);
                code[at + 2] = new CodeInstruction(OpCodes.Call, sites[s].Text ? textUnder : nodeUnder)
                {
                    labels = code[at + 2].labels,
                    blocks = code[at + 2].blocks
                };
            }
            return code;
        }

        static NodeSites.Kind KindOf(CodeInstruction i, MethodInfo node, MethodInfo text, MethodInfo setParent)
        {
            if (i.Calls(node)) return NodeSites.Kind.CreateNode;
            if (i.Calls(text)) return NodeSites.Kind.CreateText;
            if (i.Calls(setParent)) return NodeSites.Kind.MoveUnder;
            if (i.IsLdarg(0)) return NodeSites.Kind.LoadThis;
            var called = i.operand as MethodInfo;
            // A method operand with no arguments returning a node: in this constructor, the call to one of its getters.
            if (called != null && !called.IsStatic && called.GetParameters().Length == 0 && called.ReturnType == typeof(TreeNodeRender))
                return NodeSites.Kind.Getter;
            return NodeSites.Kind.Other;
        }

        // TreeNodeRender.Instantiate() followed by SetParent(parent), as one step.
        static TreeNodeRender NodeUnder(TreeNodeRender parent)
        {
            if (!Enabled || parent == null) return TreeNodeRender.Instantiate().SetParent(parent);
            Built++;
            return TreeNodeRender.InstantiateAsRoot(parent.rectTransform, false).SetParent(parent);
        }

        // TreeNodeRender.InstantiateText() followed by SetParent(parent), as one step.
        static TreeNodeRender TextUnder(TreeNodeRender parent)
        {
            if (!Enabled || parent == null) return TreeNodeRender.InstantiateText().SetParent(parent);
            Built++;
            var made = TreeNodeRender.InstantiateAsRoot(parent.rectTransform, true);
            applyLocaleFont.Invoke(null, new object[] { made });
            return made.SetParent(parent);
        }
    }
}
