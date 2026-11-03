# Inventories

## Batch inventory layouts

**Problem.** `GameItem.ValidateShapeState` runs whenever an item enters or leaves an inventory and on every tag change (`EnableTag`, `DisableTag`, `ModifyTag`). After laying out the item, it calls `Validate` on the item's whole inventory, which lays out every item in it again. Adding n items costs n × n layouts.

One save load in a large store ran 161,804 item layouts and 7,000 inventory layouts in a single frame, about 1.8 s. A single tag change on an item in a full storage bay re-lays out the whole bay, and opening a container is one such tag change (`OPENED_TAG`).

**Fix.** While `ValidateShapeState` runs, the inventory layouts it asks for are only recorded. The item's own layout, and everything else, still happens immediately. The recorded inventories are laid out once, just before Unity renders the frame (`Canvas.willRenderCanvases`). What reaches the screen is the same, but repeated requests in one frame cost one layout.

An inventory laid out directly in the meantime is dropped from the list, and an inventory whose UI was destroyed since is skipped.

## Fast selection highlight

**Problem.** While items are selected, and all the while they are dragged as a group, the game redraws the highlight under them every frame. It switches off every highlight square it lit the frame before and then lights the squares under each selected item again. Each square that goes off and on is rebuilt and its canvas re-batched, every frame. To find each item's slot, the game also looks the item up in a fresh copy of its inventory's item list, so n selected items in an inventory of m items copy n × m entries per frame. A large selection in a full storage bay stutters the whole time it is held or dragged.

Over the items' own container, a group drag is always a precise placement (elsewhere only with Shift held), and the game also redraws the placement preview every frame in the same way. To colour the preview, it checks every selected item against every item in the container, and for each of those it searches the list of selected items, which is n × m × n steps per frame. This is why dragging inside the original container stuttered most.

**Fix.** The same squares are lit in the same way, and then only the squares lit the frame before that are not lit now are switched off. A square that stays lit is not touched, so the highlight looks exactly as before and an unchanged selection costs almost nothing. Each item's slot is kept between frames while the item stays in the same inventory with the same shape, so the item list is no longer copied every frame.

The placement preview is updated in place in the same way. Whether the group fits is worked out again only when the drop position, the selection, or an item in any inventory changes. Every other condition is still checked each frame, and the drop itself still runs the game's own check.

## Check items once while loading

**Problem.** A save is decoded in eleven parts, one per inventory. Each part builds its items, restores their tags, runs about fifteen load steps on every item (machines, locks, plants, water sprites and more) and moves the items into the inventory. Every tag change and every move recomputes the item's shape and state and then redraws the item's look, and redrawing an item also redraws every container it is inside. In a store of 1,342 items that came to about 58,000 shape checks and 62,000 redraws, over a second of the load.

**Fix.** While a part is decoded, only the redraw waits. The shape, the state, placement and every load step run exactly as in the game, and none of them reads an item's look. When the part ends, each item that asked to be redrawn is redrawn once, in the order it first asked; containers it pulls in are redrawn once as well, and items destroyed in the meantime are skipped. An item's look is worked out from its current shape, state and contents, so one redraw at the end gives what the game's last redraw gives.

Each load writes one line to `Player.log`, with or without the option: `[PerformanceFixes] save load: ... ms`, the number of redraws asked for and run, and the number of items in each part's inventory. Loading the same save with the option on and off shows the time saved and that the same items were loaded.
