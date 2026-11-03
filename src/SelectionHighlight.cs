using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PerformanceFixes
{
    // Fix 7: the highlights of a selection, and the placement preview of a group drag, are updated in place instead
    // of being rebuilt every frame.
    //
    // While items are selected, and all the while they are dragged as a group, ItemMultiSelectHandler.
    // RefreshSelectionHighlight runs every frame. It switches off every highlight square it lit the frame before,
    // then, for each selected item, asks the item's inventory for its slot (GetIncompleteItemInventorySlot) and
    // lights the squares under it again. Both halves grow with the selection:
    //   - every square goes off and on again, so each one leaves and rejoins its canvas and is rebuilt, and the
    //     canvas is re-batched, every frame;
    //   - each slot lookup finds the item's index in a copy of the inventory's item list (childItems returns a new
    //     list), so n selected items in an inventory of m cost n copies of m items, every frame.
    //
    // Over the items' own inventory a group drag is always a precise placement (elsewhere only with Shift held), and
    // RefreshPrecisePreview runs every frame on top: it switches off and relights the preview squares in the same way,
    // and colours them by IsFormationValid, which checks each selected item against every item in the inventory and,
    // for each of those, looks it up in the list of selected items. That is n x m x n steps a frame.
    //
    // Both refreshes are replaced. Each lights the same squares in the same way (HighlightSlot, same colour,
    // replacing) and then switches off only the squares lit the frame before that are not lit now. Lighting a square
    // that is already lit with the same sprite changes nothing, so the squares end each frame exactly as the game
    // leaves them, and unchanged ones are left alone. In between frames are kept:
    //   - an item's slot, while the item stays in the same inventory with the same shape object; for the game's four
    //     inventory types HighlightSlot reads only the slot's inventory and shape, never its index (inventories of
    //     any other type are asked every frame, as before);
    //   - an item's shape moved by the drag offset, while its own shape object and the offset stay the same;
    //   - whether every moved shape fits in the inventory (CheckShapePlacement), while the inventory, the offset and
    //     the selection stay the same and no item anywhere has been re-laid out (GameItem.ValidateShapeState runs
    //     whenever an item enters or leaves an inventory, moves, or changes a tag). Every other condition of
    //     IsFormationValid is checked each frame. The drop itself still runs the game's own IsFormationValid.
    static class SelectionHighlight
    {
        public static bool Enabled = true;

        static FieldInfo selectedField, highlightsField, previewField;
        static MethodInfo preciseDelta;
        static readonly HashSet<TreeNodeRender> previous = new HashSet<TreeNodeRender>();
        static Dictionary<GameItemElement, SlotMarker> slots = new Dictionary<GameItemElement, SlotMarker>();
        static Dictionary<GameItemElement, SlotMarker> nextSlots = new Dictionary<GameItemElement, SlotMarker>();
        static Dictionary<GameItemElement, Moved> moved = new Dictionary<GameItemElement, Moved>();
        static Dictionary<GameItemElement, Moved> nextMoved = new Dictionary<GameItemElement, Moved>();

        // An item's shape moved by (deltaX, deltaY), and the shape it was made from.
        sealed class Moved { public GridShape from, shape; public int deltaX, deltaY; }

        // The last placement check and when it still holds (its own file, with tests).
        static readonly FormationFit<GameInventory, GameItemElement> fit = new FormationFit<GameInventory, GameItemElement>();

        // Counts item re-layouts; any change in an inventory's contents goes through one.
        static int version;

        public static void Patch(Harmony harmony)
        {
            selectedField = typeof(ItemMultiSelectHandler).GetField("selectedItems", PerformanceMod.AnyInstance);
            highlightsField = typeof(ItemMultiSelectHandler).GetField("selectionHighlights", PerformanceMod.AnyInstance);
            previewField = typeof(ItemMultiSelectHandler).GetField("precisePreviewNodes", PerformanceMod.AnyInstance);
            preciseDelta = typeof(ItemMultiSelectHandler).GetMethod("TryGetPreciseDelta", PerformanceMod.AnyInstance, null,
                new[] { typeof(GameInventory), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() }, null);
            if (selectedField == null || highlightsField == null || previewField == null || preciseDelta == null)
                throw new MissingMemberException("ItemMultiSelectHandler selection members");
            harmony.Patch(typeof(ItemMultiSelectHandler).GetMethod("RefreshSelectionHighlight", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(SelectionHighlight).GetMethod("RefreshPrefix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(ItemMultiSelectHandler).GetMethod("RefreshPrecisePreview", PerformanceMod.AnyInstance, null, new[] { typeof(GameInventory) }, null),
                new HarmonyMethod(typeof(SelectionHighlight).GetMethod("PreviewPrefix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(ItemMultiSelectHandler).GetMethod("ClearSelection", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                null, new HarmonyMethod(typeof(SelectionHighlight).GetMethod("ClearPostfix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(GameItem).GetMethod("ValidateShapeState", PerformanceMod.AnyInstance, null, Type.EmptyTypes, null),
                new HarmonyMethod(typeof(SelectionHighlight).GetMethod("LayoutPrefix", PerformanceMod.AnyStatic)));
        }

        // The game's RefreshSelectionHighlight, without switching off squares that are lit again straight away.
        static bool RefreshPrefix(ItemMultiSelectHandler __instance)
        {
            if (!Enabled) return true;
            var items = selectedField.GetValue(__instance) as List<GameItemElement>;
            var lit = highlightsField.GetValue(__instance) as List<TreeNodeRender>;
            if (items == null || lit == null) return true;

            Begin(lit);
            try
            {
                foreach (var element in items)
                {
                    var inventory = element.parentInventory;
                    var slot = inventory == null ? null : SlotOf(element, inventory);
                    if (slot == null) continue;
                    var nodes = inventory.HighlightSlot(slot, RenderHandler.BackgroundColor.Blue, true);
                    if (nodes != null) lit.AddRange(nodes);
                }
            }
            finally
            {
                End(lit);
                var kept = slots; slots = nextSlots; nextSlots = kept;
                nextSlots.Clear();
            }
            return false;
        }

        // The game's RefreshPrecisePreview, in the same way, with the placement check kept between frames.
        static bool PreviewPrefix(ItemMultiSelectHandler __instance, GameInventory inventory)
        {
            if (!Enabled) return true;
            var items = selectedField.GetValue(__instance) as List<GameItemElement>;
            var lit = previewField.GetValue(__instance) as List<TreeNodeRender>;
            if (items == null || lit == null) return true;

            Begin(lit);
            try
            {
                var args = new object[] { inventory, 0, 0 };
                if (!(bool)preciseDelta.Invoke(__instance, args)) return false;
                int deltaX = (int)args[1], deltaY = (int)args[2];
                var color = FormationValid(items, inventory, deltaX, deltaY)
                    ? RenderHandler.BackgroundColor.Green : RenderHandler.BackgroundColor.Red;
                foreach (var element in items)
                {
                    var slot = new SlotMarker();
                    slot.item = element;
                    slot.inventory = inventory;
                    slot.itemGridShape = MovedShape(element, deltaX, deltaY);
                    var nodes = inventory.HighlightSlot(slot, color, true);
                    if (nodes != null) lit.AddRange(nodes);
                }
            }
            finally
            {
                End(lit);
                var kept = moved; moved = nextMoved; nextMoved = kept;
                nextMoved.Clear();
            }
            return false;
        }

        static void Begin(List<TreeNodeRender> lit)
        {
            previous.Clear();
            foreach (var node in lit) previous.Add(node);
            lit.Clear();
        }

        // Switches off what was lit before and is not lit now.
        static void End(List<TreeNodeRender> lit)
        {
            HighlightDiff.KeepStale(previous, lit);
            foreach (var node in previous)
                if (node != null) node.image.enabled = false;
            previous.Clear();
        }

        static SlotMarker SlotOf(GameItemElement element, GameInventory inventory)
        {
            if (!IndexUnused(inventory)) return inventory.GetIncompleteItemInventorySlot(element);
            SlotMarker slot;
            if (!slots.TryGetValue(element, out slot) || slot == null || slot.item != element ||
                slot.inventory != inventory || !ReferenceEquals(slot.itemGridShape, element.modifiedShape))
                slot = inventory.GetIncompleteItemInventorySlot(element);
            if (slot != null) nextSlots[element] = slot;
            return slot;
        }

        // The inventory types whose HighlightSlot reads only the slot's inventory and shape.
        static bool IndexUnused(GameInventory inventory)
        {
            var type = inventory.GetType();
            return type == typeof(GameGridInventory) || type == typeof(GameGridScrollableInventory) ||
                type == typeof(GameSlotInventory) || type == typeof(GameCharacterRaidInventory);
        }

        // ItemMultiSelectHandler.TranslatedShape, kept while the item's shape and the offset stay the same.
        static GridShape MovedShape(GameItemElement element, int deltaX, int deltaY)
        {
            var from = element.modifiedShape;
            Moved entry;
            if (!moved.TryGetValue(element, out entry) && !nextMoved.TryGetValue(element, out entry) ||
                !ReferenceEquals(entry.from, from) || entry.deltaX != deltaX || entry.deltaY != deltaY)
            {
                entry = new Moved { from = from, deltaX = deltaX, deltaY = deltaY,
                    shape = new GridShapeBuilder(from).SetPosition(from.minX + deltaX, from.minY + deltaY).Build() };
            }
            nextMoved[element] = entry;
            return entry.shape;
        }

        // ItemMultiSelectHandler.IsFormationValid, with the placement half kept between frames. Its result is the
        // conjunction of independent checks, so splitting them does not change it.
        static bool FormationValid(List<GameItemElement> items, GameInventory inventory, int deltaX, int deltaY)
        {
            if (items.Count == 0 || inventory.IsInsertLocked()) return false;
            bool foreign = inventory != HomeInventory(items);
            if (foreign && !GeneralHelper.MayPlayerInsertInto(inventory)) return false;
            foreach (var element in items)
            {
                if (element.IsDestroyed() || element.parentInventory == null) return false;
                if (!element.MayRemove() || element.MaxNumRemove() < element.unitCount) return false;
                if (foreign && !inventory.MayHaveValidInventorySlot(element)) return false;
            }

            bool fits;
            if (fit.TryGet(inventory, deltaX, deltaY, version, items, out fits)) return fits;
            var ignored = new List<GameItem>(items);
            fits = true;
            foreach (var element in items)
                if (!inventory.CheckShapePlacement(element, MovedShape(element, deltaX, deltaY), ignored)) { fits = false; break; }
            fit.Store(inventory, deltaX, deltaY, version, items, fits);
            return fits;
        }

        // ItemMultiSelectHandler.GetCommonHomeInventory: the one inventory all the items are in, if there is one.
        static GameInventory HomeInventory(List<GameItemElement> items)
        {
            GameInventory home = null;
            foreach (var element in items)
            {
                var inventory = element.parentInventory;
                if (inventory == null) return null;
                if (home == null) home = inventory;
                else if (home != inventory) return null;
            }
            return home;
        }

        static void LayoutPrefix() { version++; }

        // The store has been left: drop every kept slot, moved shape and fit answer, which still point at its items.
        public static void Forget()
        {
            ClearPostfix();
            nextSlots.Clear();
            nextMoved.Clear();
            previous.Clear();
        }

        static void ClearPostfix()
        {
            slots.Clear();
            moved.Clear();
            fit.Forget();
        }
    }
}
