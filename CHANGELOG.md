# Changelog

## 1.2.0

- While a save loads, the redraws of each inventory are combined into one pass, so each item's look is worked out once instead of every time one of its tags is restored or a container around it is laid out. This removes tens of thousands of redraws from loading a large store. The loaded items are the same. This can be turned off in the options.
- The parts of an item's look are created directly where they belong instead of being created elsewhere and moved there, which was most of the time spent creating items. Together with the change above, a 1,342-item save loads in about 2.8 s instead of about 6 s. This can be turned off in the options.
- Each load writes its time and the number of items in each inventory to `Player.log`.
- A cost analysis of every fix in the documentation: what each one costs before and after, with the measured numbers.

## 1.1.1

- The buyer check installs only once the player's language is applied. Installing it earlier could leave the water names in English for the session, so water stopped selling in other languages.
- Containers nested more than 32 levels deep now get the game's own buying check instead of passing unchecked.
- A fix that cannot attach fully after a game update now removes the patches it attached, so it is either fully on or fully off.
- Headless tests for the decisions that need no game: `tests/run.sh`.

## 1.1.0

- The highlight under selected items is updated in place instead of being redrawn every frame, removing the stutter while a large selection is held or dragged as a group. The same goes for the placement preview when dragging within the original container, and whether the group fits is worked out again only when the drop position, the selection or an inventory changes. This can be turned off in the options.
- The options are translated into every language the game supports.

## 1.0.0 (pre-release)

First public release.

- Each container window gets its own canvas, so opening or changing one window no longer rebuilds every open window. This can be turned off in the options.
- Inventories are laid out once per frame instead of once per item change, removing a quadratic cost on save load and on tag changes in full containers. This can be turned off in the options.
- A closed window stays under the floating-window canvas instead of moving back, so opening and closing full windows (such as with the storage-preset hotkey) no longer moves their whole hierarchy between canvases. This can be turned off in the options.
- The storage-preset hotkey opens or closes its windows one per frame (the first at once), so animations no longer freeze while a preset opens. This can be turned off in the options.
- Container windows are hidden by switching off their canvas instead of deactivating thousands of components, so opening and closing a full window is nearly instant after the first time. This can be turned off in the options.
- The customer buying check on containers runs once per item instead of once per item per level of nesting. Results are identical to the game's.
- Frames over 100 ms are logged with the window opened just before.
- Optional long-frame profiler (off by default) breaks slow frames down into scripts, UI rebuild and rendering, and the slowest game methods, including everything the shop's main update calls.
