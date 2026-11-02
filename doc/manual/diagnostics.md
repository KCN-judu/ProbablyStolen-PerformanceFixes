# Diagnostics

## Long-frame log

With **Log long frames** on, frames longer than 100 ms are written to the game's `Player.log` as `[PerformanceFixes] long frame: ...`, with the container window opened just before, if any, and counters for each fix. It costs nothing.

`Player.log` is in `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen`.

## Long-frame profiler

**Profile long frames** goes further. While it is on, every per-frame callback of the game's MonoBehaviours, every layout pass (`Validate`) and everything the shop's main update calls, four levels deep, is timed, along with Unity's UI rebuild and rendering. Each frame over 100 ms is then logged with its slowest methods:

```
[PerformanceFixes] profile of a 290 ms frame: scripts and engine 284 ms, UI rebuild and rendering 6 ms; slowest: StoreUIManager.Update 281.8 ms x1, ...
```

Times are inclusive: a method that calls others includes them. The profiler adds a little overhead, so leave it off for normal play. It never lets a failure reach the game: if it cannot attach to a method, it skips it, and if it cannot start, it removes everything it attached.

## When a fix cannot attach

After a game update, a fix may no longer find a method it patches. It then removes the patches it had already attached, so it is either fully on or fully off, and writes one line to `Player.log`: `[PerformanceFixes] <fix> is off: ...`. The other fixes keep working.

## Regenerating the method lists

The timed methods are listed in generated files, so the game's mod scanner sees every type named in code and asks for no permission:

- `tools/GenProbe.cs` writes `src/Targets.g.cs`, the per-frame callbacks and layout passes.
- `tools/GenCallees.cs` writes `src/Callees.g.cs`, everything the given methods call, to a given depth.

Both leave out the game's mod loader, mod security and Steam integration, which mods may not patch. Regenerate them after a game update.
