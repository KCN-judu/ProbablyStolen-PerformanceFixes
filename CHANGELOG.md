# Changelog

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
