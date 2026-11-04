# Windows

Four fixes make opening and closing full storage windows smooth. They were found with the profiler: the storage-preset hotkey, which opens four full windows at once, froze the game for about 300 ms.

## Separate canvas per window

**Problem.** The game puts every floating window under one canvas, `FloatingWindowCanvas`. Unity rebuilds a canvas as a whole: whenever anything in it changes, every graphic in every open window is re-batched. A window opening, an item moving or a slot lighting up under the mouse is enough. With a few large storage windows open, that is thousands of graphics per change.

**Fix.** Before a container window is shown, it gets a nested canvas, so a change inside it rebuilds only that window. The nested canvas keeps the parent's draw order, copies the root's reference pixels-per-unit and shader channels so sprites and text look the same, and has its own raycaster so its contents still take clicks.

The game finds window bounds through `Canvas.rootCanvas` and positions windows with its own window canvas, so placement and dragging are unaffected. Tooltips and menus are left alone.

## Keep closed windows in place

**Problem.** When a window opens, the game moves it from its home, the scene's window canvas, under the floating-window canvas (`UIFamilyLayer.Adopt`). When it closes, the game moves it back (`ReleaseWindow`). Moving a hierarchy between canvases makes every graphic and text in it re-register and rebuild, so a full storage window costs tens of milliseconds each way. This was about 140 ms of the 280 ms preset stutter.

**Fix.** A closed window stays under the floating canvas. The next time it opens, the game finds it already in place and only brings it to the front. A window is kept only when its home is in the same scene as the floating canvas, so a scene change destroys it together with its home, exactly as before. A window being destroyed goes home first as usual.

## Hide closed windows instantly

**Problem.** Hiding a window deactivates its GameObject, and showing it activates it again. For a full storage window that means thousands of components switched off and on again, about 110 ms for one large window each way.

**Fix.** Switching off the window's own canvas hides it just as well and costs almost nothing: nothing is drawn, and its raycaster is off, so it takes no clicks. The game reads a window's state only through `PixelWindow.IsVisible` and changes it only in `Show` and `Hide`, so for container windows that have their own canvas and stay in place:

- `Hide` performs the game's own steps in the same order: it remembers the window position, releases the window and removes it from the open-window list. The only difference is that it switches the canvas off instead of deactivating the object.
- `Show` switches the canvas back on first. After that, the game's `SetActive(true)` finds the object already active and does nothing. A locked window, such as a container whose inventory is shown inside a machine slot, is left hidden, as the game's own `Show` leaves it.
- `IsVisible` answers false for a window hidden this way.

Each window still pays the full cost the first time it opens. Turning the option off restores every such window to an ordinary, deactivated one.

## Open preset windows one by one

**Problem.** The storage-preset hotkey opens or closes every window of the preset in one frame, so any remaining cost freezes every animation for its whole length.

**Fix.** The first window opens or closes at once, so the key answers immediately, and the others follow on the next frames, one each. Each window's tab selection is applied right after its own opening, as the game does. Pressing the hotkey again while some are still pending finishes them first, so the game always sees a settled state.

## Disclaimer: windows of other mods

These fixes change how the game's own floating windows (`PixelWindow`) close and open again. Windows that other mods build with the game's custom UI (`CustomUIManager.CreateWindow`), or from their own objects, are not touched.

A mod that makes its own `PixelWindow` can be affected in one case. With Hide closed windows instantly on, a closed container window, or one the mod gave its own canvas, stays active with its canvas switched off instead of being deactivated. The game's `IsVisible`, `Show` and `Hide` work as usual on it. A mod that checks `activeSelf` instead of `IsVisible`, opens the window with `SetActive(true)` instead of `Show`, or refreshes it in `OnEnable`, may find that its window does not open again, or opens with old contents. We do not work around that.

If a window from another mod misbehaves, turn off Hide closed windows instantly in the options. Every window hidden this way becomes an ordinary hidden window at once, and a window that closes is then deactivated, as in the base game. Please tell us which mod it was, so it can be listed here.

To mod authors: read a window's state with `PixelWindow.IsVisible()`, open and close it with `Show()` and `Hide()`, as the game itself does, and do not rely on `OnEnable` or `OnDisable` to notice it. Your window then works the same with and without this mod.
