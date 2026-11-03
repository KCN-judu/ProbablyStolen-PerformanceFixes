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

**Problem.** A save is decoded in eleven parts, one per inventory. Each part builds its items, restores their tags, runs about fifteen load steps on every item (machines, locks, plants, water sprites and more) and moves the items into the inventory. Every tag change and every move recomputes the item's shape and state and then redraws the item's look; redrawing an item also redraws every container it is inside, and every inventory layout redraws all its items again. In a store of 1,342 items that came to about 58,000 shape checks and 62,000 redraws, over a second of the load: each item's look worked out about 45 times.

**Fix.** The redraws of one part are combined into a single pass, at the end of that same part of the load: each item that asked to be redrawn is redrawn once, in the order it first asked, then the containers around it once each; items destroyed during the part are skipped. Everything else runs exactly where and as often as in the game: shapes and states, placement, inventory layouts (they also set sizes and slot positions that later shape computations read) and every load step, none of which reads an item's look. An item's look is worked out from its current shape, state and contents, so the one pass gives what the game's last redraw gives.

Each load writes one line to `Player.log`, with or without the option: `[PerformanceFixes] save load: ... ms`, the number of redraws asked for and run, and the number of items in each part's inventory. Loading the same save with the option on and off shows the time saved and that the same items were loaded.

## Build item UI in place

**Problem.** Every item's look is made of thirteen parts. The game creates twelve of them in a shared temporary place and then moves each one to where it belongs inside the item. Moving a part inside the game's interface is slow, about 80 µs each whatever the part holds, so a save of 1,342 items spent most of 2.3 s just moving parts, and the same happens whenever many items are created.

**Fix.** Each of those twelve parts is created directly where it belongs, and the game's own move then finds it already in place. The part ends up with the same parent, the same place among its siblings and the same position, rotation and scale as before, and it starts up the same way, because everything it is created under has just been made by the item and is active. The item's first part is created as before. If a game update changes how many parts are made this way, the option does not install and `Player.log` says so.

With this and the option above, the same 1,342-item save loaded in about 2.8 s instead of about 6 s, with the same items in every inventory.
