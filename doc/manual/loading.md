# Loading

Loading a save goes through three stages: the store's scene is set up, the save is read and its items are made, and the first frame is drawn. The fixes on this page and in [Inventories](inventories.md) remove work from the last two. Setting up the scene is almost all Unity's own work and is left as it is.

On a store of 1,342 items, starting the game fresh before each load and loading the same save each time:

| | Click to first frame |
| --- | --- |
| Without this mod | about 7.8 s and 8.2 s |
| With this mod | 4.64 s and 4.77 s |

The times with this mod come from its own load timeline (see [Diagnostics](diagnostics.md)). Without the mod there is no timeline, so those times come from the game's stall watchdog, which logged the main thread blocked for 7.5 s and 7.9 s, plus the 0.3 s that the two logs differ by on loads where both were written.

## Lighter store preload

**Problem.** Almost all of a save is one entry, the store, which holds the contents of every inventory: 13 MB for this store. Before the save loads, the game reads that whole entry once just to see whether the display case and the fixtures are upgraded, and then drops it. The real load reads it again.

**Fix.** That early look now reads only the store's upgrades. Easy Save, the game's save system, is told for the moment that the store has only that one field, so it reads the upgrades exactly as before and passes over the rest, as it does with any field it does not know. Nothing else of the early copy was ever read. The real load is not changed.

## Skip unchanged settings

**Problem.** Each time you enter the store, the game writes each of its settings and each key binding to its settings file one at a time: 57 writes. Each write reads the whole file, puts in the one value, writes a new file and swaps it in, and nearly every value written is the one already in the file. This took about half a second of every load.

**Fix.** Before each of those writes, the value is read back from the file with the same settings the game writes it with. If the file already holds that value, the write is left out, since it would give the same file. A setting that is missing, different or cannot be read back is written exactly as before. Writes anywhere else, including rebinding a key in the options, are not changed.

## Free the store when leaving

**Problem.** A store's items, inventories and windows all point at each other, so one reference to any of them kept anywhere keeps the whole store in memory. After leaving the store, for the main menu say, several such references stayed: the lookup tables of the last save, the list of window roots, the game's `current` and `Instance` fields of the store's managers, and some caches. Each load in the same session then started with the previous store still in memory.

**Fix.** When another scene replaces the store, these are let go and memory is collected once while the menu is idle. Each reference is cleared only where the game no longer reads it: the save tables are replaced as at the start of every save and load, only windows already destroyed are taken off the list, and a manager field is cleared only while it points at a destroyed manager, which Unity already treats as nothing. `Player.log` gets one line with the memory before and after.
