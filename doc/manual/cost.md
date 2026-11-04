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
| k | UI parts per item (13), or per window, inventory or machine panel |
| E | UI elements built: items, windows, inventories, machine panels |
| c | collections built for one search under an item (a list, a set, a queue and the result) |
| S | size of the store's entry in the save: 13 MB for the store measured here |
| u | the store's upgrades, the part of S the early look needs |
| K | settings and key bindings written on entering the store (57) |
| F | size of the settings file (about 8 KB) |
| R | references to a store that is left: lookup tables, window list, manager fields, caches |
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
| Build UI in place | k creations and k − 1 moves per element: O(E · k) moves | k creations, no moves | yes |
| Lighter shape checks | each shape check of an item with nothing under it searches anyway and builds c collections | no search and nothing built for such an item; the same search as before for any other | yes |
| Fast selection highlight | O(m · n) list copies and s squares off and on, every frame; inside the original container also O(m² · n) every frame | no list copies, only changed squares touched; the O(m² · n) check only when the drop position or contents change | yes |
| Lighter store preload | the store read twice in full: 2 · O(S), every string and object of it built twice | the early look passes over S and builds only u: O(S) to pass over, O(u) built | yes |
| Skip unchanged settings | K full rewrites of the settings file: O(K · F) read and O(K · F) written, K file swaps | K reads, O(K · F); a write only for a setting that changed | yes |
| Free the store when leaving | the left store stays in memory until the next one replaces it | O(R) to let go, one collection while the menu is idle | yes |
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

**Measured.** On a store of 1,342 items: 61,551 UI requests became 1,343 runs (11 more were for items destroyed during the load, and skipped). With the long-frame profiler on, the time spent working out item UI fell from 576 ms to 57 ms. Without the profiler, and with the other fixes on, the save load took 2,718 ms and 2,761 ms with the option off and on, a difference within the spread between loads: once the other fixes have made each UI walk cheap, removing the repeated ones no longer shows. An earlier comparison of 3,645 ms against 2,903 ms was made with the profiler on, which made every walk slower, and overstated the gain.

**Result.** Identical. An item's UI is worked out from its current shape, state and contents, so working it out once at the end gives what the game's last time gives. Loading the same save with the option off and on put the same number of items in every inventory.

### Build UI in place

**Game.** Each item's UI has k = 13 parts, and windows, inventories and machine panels are built the same way. All but the first part of each element are created in a shared temporary place and then each is moved to where it belongs. Creating is unavoidable; moving costs a constant per move, about 73 µs for a bare part inside the game's interface and about 28 µs outside it, whatever the part holds. That is k − 1 moves per element, O(E · k) moves for E elements.

**Fix.** In 33 builders, 132 kinds of part are created directly where they belong, so their moves are gone. The creations remain, so **the order of growth is still O(E · k)**; what goes away is one of the two per-part steps, and the more expensive one. Moves the game makes for other reasons, such as laying out an inventory's background, are not touched: about 8,000 of them remain while the 1,342-item store loads.

**Measured.** With items alone, creating the UI of 1,342 items fell from 2,040 ms to 363 ms and the whole save load from 4,655 ms to 2,833 ms. Adding windows, inventories and machine panels raised the parts created in place from 16,236 to 20,053 per load and took about 0.1 s to 0.25 s more off it.

**Result.** Identical: each part ends with the same parent, the same place among its siblings and the same position, rotation and scale, and it starts up the same way.

### Lighter shape checks

**Game.** Every tag change and every move works out the item's shape and state, t times per item, O(N · t) in all: 58,148 times for the 1,342-item store. Each time, the game searches everything under the item for inventories, and for that builds c = 4 collections and walks them, even for an item with nothing under it.

**Fix.** For an item with neither an inventory window nor an examine window the search returns the same empty answer at once, with nothing built. **The number of shape checks is unchanged, O(N · t)**, and so is the rest of each one, including the copy of the item's tags; only the constant of the search goes away for such items. Items with anything under them are searched as before.

**Measured.** 56,211 of the 58,148 searches were skipped. With the load timed method by method, the shape computation's own time fell from 118 ms to 85 ms to 89 ms; part of both is the timing itself.

**Result.** Identical: the empty answer is the one the search gives for an item with nothing under it, and the caller only reads it.

### Fast selection highlight

**Game.** While items are selected, and while they are dragged, the game every frame switches off all s lit highlight squares and lights them again, so each one rebuilds and its canvas is re-batched. To find each item's slot it searches a fresh copy of the inventory's item list: O(m · n) per frame. Over the items' own container, a group drag also checks every selected item against every item in the inventory, and for each of those searches the list of selected items: O(m² · n) per frame.

**Fix.** The same squares are lit, but only squares whose state changes are touched, and each item's slot is kept while the item stays put, so no lists are copied. The fit check itself is not made faster: **it still costs O(m² · n) when it runs**, but it runs again only when the drop position, the selection or an inventory's contents change, instead of every frame. Every other condition of the check still runs every frame.

**Measured.** Not timed. Holding and dragging a large selection no longer stutters, inside the original container as well.

**Result.** Identical: the squares end every frame as the game leaves them, and the drop itself still runs the game's own check.

## Loading

### The whole load

On the 1,342-item store, starting the game fresh before each load and loading the same save each time, click to first frame took about 7.8 s and 8.2 s without this mod and 4.64 s and 4.77 s with it. Without the mod there is no load timeline, so those two times are the main-thread stalls the game's own watchdog logged (7.5 s and 7.9 s) plus 0.3 s, the difference between the watchdog and the timeline on loads where both were written.

| Stage | With this mod | What it is |
| --- | --- | --- |
| Fade to black | about 0.2 s | the game's fade and its first frames |
| Scene set up | about 1.9 s | almost all Unity reading the store's scene; no fix here |
| Before the save load | about 0.17 s | the early look at the store, among others |
| Save load | about 2.2 s to 2.3 s | reading the save and making every item, inventory and window |
| To the first frame | about 0.15 s | the rest of the store's start, the settings among it, and the first frame |

### Lighter store preload

**Game.** Before the save loads, the game reads the store's whole entry, S bytes, into a new store object to look at u of its upgrades, and drops it; the real load then reads the whole entry again. Both reads build every string and object in S, mostly the JSON of every inventory: 2 · O(S) in time and in what is built.

**Fix.** The early look passes over everything but the upgrades. Passing over still reads every character of S, so **the order of growth stays O(S)**; what goes away is building the strings and objects of the rest, O(S) of allocation, leaving O(u). The real load is unchanged.

**Measured.** With the load timed method by method, the early look fell from 131 ms to 147 ms down to 79 ms to 81 ms.

**Result.** Identical: the upgrades are read by Easy Save's own reader exactly as before, and nothing else of the early copy is read by the game.

### Skip unchanged settings

**Game.** Entering the store writes K = 57 settings and key bindings one at a time. Each write reads the settings file, F bytes, merges in its one value, writes a temporary file and swaps it in: O(K · F) read and O(K · F) written, and K file swaps, which cost far more than the bytes. Nearly every value is the one already in the file.

**Fix.** Each value is first read back: still K reads, O(K · F), but **only a setting that changed is written**, so in the usual case nothing is written and nothing is swapped.

**Measured.** With the load timed method by method, the settings step fell from 496 ms to 10 ms: 0 written, 57 left out. The time from the end of the save load to the first frame fell from about 540 ms to about 155 ms.

**Result.** Identical: a write is left out only where the file already holds the same key with the same value, so the file ends the same.

### Free the store when leaving

**Game.** After leaving the store, R references keep pointing at it, and any one of them keeps the whole store, all its items, inventories and windows, in memory until the next store replaces each reference. Each later load in the same session starts with that memory still in use.

**Fix.** When another scene replaces the store, the R references are let go, O(R), and memory is collected once while the menu is idle. **This fix does not make a load faster by itself**: it stops the memory of each store left from staying in use until the next one.

**Measured.** In the store, 237 MB of managed memory were in use; after leaving it and a full collection, 158 MB, with one left-behind holder found, a list of three destroyed cameras in the game's own letterbox manager.

**Result.** Identical: each reference is cleared only where the game no longer reads it.

## Customers

### Fast buyer check

**Game.** To decide whether a customer buys a container, the game lists every item anywhere inside it and runs the full check on each one, and each of those lists and checks everything inside itself again. An item k levels below the container is reached once for every combination of the containers between them, so it can be checked up to 2^(k−1) times, and every listing builds a new list. For the shallow nesting of most stores this looks roughly quadratic; it grows much faster with depth.

**Fix.** The same rule is evaluated bottom-up once per item per call: O(N) for N items inside, plus one walk over the contents. Answers are kept only within that one call. Contents nested more than 32 levels deep in the game's item tree get no answer from this rule, and the game's own check runs for that call instead.

**Measured.** Not timed separately. The rule is checked outside the game against a plain version of the game's rule on 2,000 random stores, with the same answer every time (see [Building](building.md)).

**Result.** Identical: the same answers for the current state.
