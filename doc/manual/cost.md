# Cost analysis

How much work each fix removes, in terms of the sizes involved. For each fix: what the game does and what that costs, what the fix does and what it costs instead, what was measured, and whether the result is the same as the game's.

Most fixes remove repeated work: the same item, inventory or highlight worked out again and again with the same inputs. Where the order of growth stays the same and only a repeated pass or a constant per step goes away, this page says so. Times were measured in game on one large store and are given to show scale, not as guarantees for every store.

## Sizes

| Symbol | Meaning |
| --- | --- |
| N | items involved, for example all items in a save |
| n | items in one inventory |
| t | tag changes and moves per item while a save loads |
| d | nesting depth: containers inside containers |
| P | containers around the items being worked on |
| k | UI parts per item (13) |
| m | selected items |
| s | highlight squares lit |
| W | open windows |
| g | graphics and components in one window |

## At a glance

| Fix | Game | With the fix | Same result |
| --- | --- | --- | --- |
| Separate canvas per window | O(g · W) per change | O(g) per change | yes |
| Keep closed windows in place | O(g) per open and per close | O(g) on the first open, then constant | yes |
| Hide closed windows instantly | O(g) per open and per close | O(g) on the first open, then constant | yes |
| Open preset windows one by one | O(g · W) in one frame | the same total, O(g) per frame | yes |
| Batch inventory layouts | O(n) per change, O(n²) to fill an inventory | O(n) per inventory per frame | yes |
| Check items once while loading | each item's UI worked out about t times, each time with its containers | each item once, then each container once: O(N + P) per part | yes |
| Build item UI in place | k creations and k − 1 moves per item | k creations, no moves | yes |
| Fast selection highlight | O(m · n) list copies and s squares off and on, every frame; inside the original container also O(m² · n) every frame | no list copies, only changed squares touched; the O(m² · n) check only when the drop position or contents change | yes |
| Fast buyer check | an item k levels deep checked up to 2^(k−1) times | each item checked once: O(N) | yes |

## Windows

### Separate canvas per window

**Game.** Every floating window is under one canvas, and Unity rebuilds a canvas as a whole. Any change in any window, an item moving or a slot lighting up under the mouse, re-batches every graphic of every open window: O(g · W) per change.

**Fix.** Each container window gets its own nested canvas, so a change re-batches only its own window: O(g) per change. The order of growth per window is the same; the factor W, the other open windows, goes away.

**Result.** Identical: same draw order, pixels-per-unit and shader channels, and clicks still reach the window.

### Keep closed windows in place

**Game.** Opening a window moves it from its home under the floating-window canvas, and closing it moves it back. Each move makes every graphic in the window re-register: O(g) per open and per close.

**Fix.** A closed window stays where it is. The first open still costs O(g); after that, opening only brings it to the front, a constant amount of work.

**Measured.** About 140 ms of the 280 ms stutter of the storage-preset hotkey, which opens four full windows at once.

**Result.** Identical. A window is kept only within its own scene and goes home before it is destroyed.

### Hide closed windows instantly

**Game.** Hiding a window deactivates it, and showing it activates it again: every component in it is switched off and on, O(g) each way.

**Fix.** The window's own canvas and raycaster are switched off instead: constant work per hide and per show. The first open of each window still costs O(g).

**Measured.** About 110 ms each way for one large storage window before; nearly instant after its first open.

**Result.** Identical: the game's own hide steps run in the same order, and the window answers "not visible" as before.

### Open preset windows one by one

**Game.** The storage-preset hotkey opens or closes all W windows of a preset in one frame: O(g · W) in that frame.

**Fix.** The same windows open or close one per frame, the first at once. **This fix does not reduce the work**: the total stays O(g · W). It spreads it over W frames, so no single frame does more than O(g) and animations keep moving.

**Result.** Identical: each window's tab selection is applied right after its own opening, and pressing the hotkey again finishes any pending windows first.

## Inventories

### Batch inventory layouts

**Game.** Every item that enters or leaves an inventory, and every tag change on an item, lays the item out and then lays out its whole inventory again: O(n) per change. Filling an inventory of n items costs n inventory layouts of growing size, O(n²); a tag change on one item in a full storage bay lays out the whole bay.

**Fix.** The inventory layouts asked for while an item is laid out are collected, and each inventory is laid out once, just before the frame is drawn: O(n) per inventory per frame however many changes it had. The item's own layout and everything else still run at once.

**Measured.** One save load ran 161,804 item layouts and 7,000 inventory layouts in a single frame, about 1.8 s.

**Result.** Identical on screen, because the layout happens before the frame is drawn. This is the one fix that moves work to a later point in the frame rather than combining it in the same call, because in the game's design the inventory layout is triggered from inside every item change and cannot be combined there.

### Check items once while loading

**Game.** A save loads in parts, one per inventory. While a part loads, every tag change and every move of an item recomputes its shape and state and then works out its UI again, about t times per item. Working out an item's UI also works out the UI of every container around it, and every inventory layout works out the UI of all its items again. With a container nesting depth of d, an item's UI is worked out O(t · d) times, the same inputs nearly every time but the last.

**Fix.** The UI work of one part is combined into a single pass at the end of that same part: each item whose UI was asked for is worked out once, then each container around them once. That is O(N + P) per part. Shapes, states, placement, inventory layouts and every load step still run exactly as often as in the game, so their own cost, O(N · t) shape computations, is unchanged.

**Measured.** On a store of 1,342 items: 61,551 UI requests became 1,343 runs (11 more were for items destroyed during the load, and skipped). The time spent working out item UI fell from 576 ms to 57 ms, and the shape computation, which used to include it, from 991 ms to 507 ms.

**Result.** Identical. An item's UI is worked out from its current shape, state and contents, so working it out once at the end gives what the game's last time gives.

### Build item UI in place

**Game.** Each item's UI has k = 13 parts. Twelve of them are created in a shared temporary place and then each is moved to where it belongs inside the item. Creating is unavoidable; moving costs a constant per move, about 73 µs for a bare part inside the game's interface and about 28 µs outside it, whatever the part holds. That is 12 moves per item, O(N · k) moves for N items.

**Fix.** Each part is created directly where it belongs, so the moves are gone. The creations remain, so **the order of growth is still O(N · k)**; what goes away is one of the two per-part steps, and the more expensive one.

**Measured.** Creating the UI of 1,342 items fell from 2,040 ms to 363 ms. The whole save load went from 4,655 ms to 2,833 ms.

**Result.** Identical: each part ends with the same parent, the same place among its siblings and the same position, rotation and scale, and it starts up the same way.

### Fast selection highlight

**Game.** While items are selected, and while they are dragged, the game every frame switches off all s lit highlight squares and lights them again, so each one rebuilds and its canvas is re-batched. To find each item's slot it searches a fresh copy of the inventory's item list: O(m · n) per frame. Over the items' own container, a group drag also checks every selected item against every item in the inventory, and for each of those searches the list of selected items: O(m² · n) per frame.

**Fix.** The same squares are lit, but only squares whose state changes are touched, and each item's slot is kept while the item stays put, so no lists are copied. The fit check itself is not made faster: **it still costs O(m² · n) when it runs**, but it runs again only when the drop position, the selection or an inventory's contents change, instead of every frame. Every other condition of the check still runs every frame.

**Measured.** Not timed. Holding and dragging a large selection no longer stutters, inside the original container as well.

**Result.** Identical: the squares end every frame as the game leaves them, and the drop itself still runs the game's own check.

## Customers

### Fast buyer check

**Game.** To decide whether a customer buys a container, the game lists every item anywhere inside it and runs the full check on each one, and each of those lists and checks everything inside itself again. An item k levels below the container is reached once for every combination of the containers between them, so it can be checked up to 2^(k−1) times, and every listing builds a new list. For the shallow nesting of most stores this looks roughly quadratic; it grows much faster with depth.

**Fix.** The same rule is evaluated bottom-up once per item per call: O(N) for N items inside, plus one walk over the contents. Answers are kept only within that one call. Contents nested more than 32 levels deep get no answer from this rule, and the game's own check runs for that call instead.

**Measured.** Not timed separately. The rule is checked outside the game against a plain version of the game's rule on 2,000 random stores, with the same answer every time (see [Building](building.md)).

**Result.** Identical: the same answers for the current state.
