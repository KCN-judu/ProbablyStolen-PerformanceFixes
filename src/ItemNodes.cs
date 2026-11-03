using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PerformanceFixes
{
    // Fix 9: UI nodes are created directly under their own parents instead of being created in a shared temporary
    // parent and then moved.
    //
    // The game's UI elements (items, windows, inventories, machine panels and their widgets, buttons, text) build their
    // nodes in their constructors. Each node is created with TreeNodeRender.Instantiate() or InstantiateText(), which put
    // it in RenderHandler's temporary root, and is then moved under one of the element's other nodes with SetParent.
    // Moving a node in the UI hierarchy is the expensive part: about 80 µs each inside the game's canvas, whatever the
    // node holds. A save load of 1,342 items spent most of 2.3 s moving nodes of items alone, and after those, about
    // 0.9 s more moving the nodes of windows, inventories and machine panels.
    //
    // Each create-then-move pair is made one step: the node is created with InstantiateAsRoot under its final parent,
    // the font of a text node is applied as InstantiateText does, and the game's own SetParent then runs as before,
    // finding the node already in place (it still sets the node's parent and depth as usual). A pair counts only as
    // the four instructions "create, load this, get one of this element's nodes (through a getter that only reads a
    // field, or the field itself), move"; the list below holds the constructors checked against the game's code
    // where every such parent is a node the same constructor (or the base constructor before it) has just created,
    // and where nothing before the last pair deactivates, hides or moves a node elsewhere. Every parent is therefore an
    // active node, so the new node starts up exactly as it would in the temporary root; it ends with the same parent,
    // the same position among its siblings and the same local position, rotation and scale. Each element's first node
    // is created as before.
    //
    // The pairs are found by NodeSites (its own file, with tests). A constructor that no longer has the expected number
    // of pairs, after a game update say, is left as it is, and Player.log names it.
    static class ItemNodes
    {
        public static bool Enabled = true;
        public static int Built;

        static MethodInfo applyLocaleFont;
        static readonly Dictionary<MethodBase, int> expected = new Dictionary<MethodBase, int>();

        static void Site(ConstructorInfo ctor, int pairs)
        {
            if (ctor != null) expected[ctor] = pairs;
        }

        // The constructors checked against the game's code, with their number of pairs.
        static void ListConstructors()
        {
            expected.Clear();
            Site(typeof(global::AdvButtonElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::UnityEngine.Sprite), typeof(global::System.String), typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Action), typeof(global::System.String) }, null), 2);
            Site(typeof(global::BannerLocationElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::System.String), typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Action), typeof(global::BannerLocationElement.Color) }, null), 3);
            Site(typeof(global::ButtonElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Action), typeof(global::ButtonElement.Color), typeof(global::System.String) }, null), 2);
            Site(typeof(global::ColoredPixelElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Boolean) }, null), 2);
            Site(typeof(global::GameCharacterItem).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::System.String), typeof(global::System.String), typeof(global::System.String) }, null), 12);
            Site(typeof(global::GameCharacterRaidInventory).GetConstructor(PerformanceMod.AnyInstance, null, Type.EmptyTypes, null), 3);
            Site(typeof(global::GameGridInventory).GetConstructor(PerformanceMod.AnyInstance, null, Type.EmptyTypes, null), 4);
            Site(typeof(global::GameGridScrollableInventory).GetConstructor(PerformanceMod.AnyInstance, null, Type.EmptyTypes, null), 7);
            Site(typeof(global::GameItemElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::System.String), typeof(global::System.String), typeof(global::PixelWindow) }, null), 12);
            Site(typeof(global::GameSlotInventory).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Boolean), typeof(global::System.Boolean) }, null), 5);
            Site(typeof(global::GridPixelElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Boolean) }, null), 2);
            Site(typeof(global::IndicatorElement).GetConstructor(PerformanceMod.AnyInstance, null, Type.EmptyTypes, null), 2);
            Site(typeof(global::MachineryButton).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::System.Action), typeof(global::System.Int32), typeof(global::System.Int32) }, null), 3);
            Site(typeof(global::MachineryIntake).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::PixelElement), typeof(global::System.Boolean), typeof(global::System.Boolean) }, null), 3);
            Site(typeof(global::MachineryLabel).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String) }, null), 3);
            Site(typeof(global::MachineryPanel).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::PixelElement), typeof(global::System.String) }, null), 3);
            Site(typeof(global::MachineryPanelRow).GetConstructor(PerformanceMod.AnyInstance, null, Type.EmptyTypes, null), 2);
            Site(typeof(global::MachineryProgressBar).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Single), typeof(global::System.Int32) }, null), 2);
            Site(typeof(global::MachineryRadioButton).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::System.Boolean), typeof(global::System.Action<global::System.Boolean>) }, null), 2);
            Site(typeof(global::MachineryRangeSlider).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Single), typeof(global::System.Single), typeof(global::System.Int32) }, null), 6);
            Site(typeof(global::MachinerySlider).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Single), typeof(global::System.Int32) }, null), 4);
            Site(typeof(global::MachineryStack).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Boolean), typeof(global::System.Int32) }, null), 1);
            Site(typeof(global::MachineryTabs).GetConstructor(PerformanceMod.AnyInstance, null, Type.EmptyTypes, null), 3);
            Site(typeof(global::MachineryText).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::System.Int32), typeof(global::TMPro.HorizontalAlignmentOptions), typeof(global::System.Boolean) }, null), 1);
            Site(typeof(global::PixelWindow).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Boolean), typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.String), typeof(global::WindowStyle) }, null), 13);
            Site(typeof(global::PushButtonElement).GetConstructor(PerformanceMod.AnyInstance, null, Type.EmptyTypes, null), 4);
            Site(typeof(global::RichTextElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Boolean), typeof(global::System.Int32), typeof(global::System.Int32), typeof(global::System.Boolean) }, null), 6);
            Site(typeof(global::ScrollElement).GetConstructor(PerformanceMod.AnyInstance, null, Type.EmptyTypes, null), 3);
            Site(typeof(global::ScrollableRichTextElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Boolean), typeof(global::System.Boolean) }, null), 9);
            Site(typeof(global::SpritePixelElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String) }, null), 1);
            Site(typeof(global::SpritePixelElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.String), typeof(global::System.String) }, null), 1);
            Site(typeof(global::StealPixelElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Boolean) }, null), 1);
            Site(typeof(global::TagElement).GetConstructor(PerformanceMod.AnyInstance, null, new[] { typeof(global::System.Int32), typeof(global::System.Boolean) }, null), 5);
        }

        public static void Patch(Harmony harmony)
        {
            applyLocaleFont = typeof(TreeNodeRender).GetMethod("ApplyLocaleFont", PerformanceMod.AnyStatic, null, new[] { typeof(TreeNodeRender) }, null);
            if (applyLocaleFont == null) throw new MissingMethodException("TreeNodeRender.ApplyLocaleFont");
            ListConstructors();
            var transpiler = new HarmonyMethod(typeof(ItemNodes).GetMethod("Transpiler", PerformanceMod.AnyStatic));
            var skipped = new List<string>();
            int done = 0, pairs = 0;
            foreach (var target in expected)
            {
                try { harmony.Patch(target.Key, null, null, transpiler); done++; pairs += target.Value; }
                catch (Exception e) { skipped.Add(target.Key.DeclaringType.Name + " (" + (e.InnerException ?? e).Message + ")"); }
            }
            if (done == 0) throw new InvalidOperationException("no constructor could be changed: " + string.Join("; ", skipped.ToArray()));
            UnityEngine.Debug.Log("[PerformanceFixes] UI built in place: " + pairs + " node(s) in " + done + " constructor(s)" +
                (skipped.Count > 0 ? "; left as they are: " + string.Join("; ", skipped.ToArray()) : ""));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
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
            int want;
            if (!expected.TryGetValue(original, out want) || sites.Count != want)
                throw new InvalidOperationException("expected " + want + " node pairs, found " + sites.Count);

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
            var field = i.operand as FieldInfo;
            if (field != null && field.FieldType == typeof(TreeNodeRender) && !field.IsStatic && i.LoadsField(field)) return NodeSites.Kind.ParentField;
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
