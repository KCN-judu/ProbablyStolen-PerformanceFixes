# Customers

## Fast buyer check

**Problem.** When the game decides whether the customer will buy an item (`StoreClient.IsClientBuyingThisItem`), it also checks a container's contents. It lists every item anywhere inside, runs the full check on each one, and each of those lists and checks its own contents again. An item k levels below the container can be checked up to 2^(k−1) times, and every level allocates a new list; for shallow storage that looks roughly quadratic, and it grows much faster with depth.

The game runs this check when an item is placed on the counter, when it decides whether something can be sold, and in the negotiation screen, once for every item in the back inventory.

**Fix.** The rule, read from the game's code:

- an item is **bought** when it is acceptable on its own (the game's own check with `ignoreContents`) and, if its contents are checked, everything inside it is bought;
- contents are **checked** unless the item is currently locked, has no content window, or is an advanced gun.

`IsClientBuyingThisItem` and `IsClientRefusingItem`, in the forms that include contents, are patched to evaluate exactly that rule bottom-up, once per item per call. Answers are reused only within that call, never across calls or frames, so the result is always the game's own for the current state. The single-item form runs unchanged.

Contents nested more than 32 levels deep get no answer from the rule. For such a call the game's own check runs instead, and its checks of the items inside come back through the fix one level down each time, so even then the answer is the game's.

**When it starts.** The game's own check uses a list of localized water names that the game fills in the first time it is needed. Installing the fix fills it in too, and mods start before the game applies the player's language. So the fix waits until the language the game chose at startup is applied, and only then installs. If that has not happened within five minutes, the fix stays off for the session and `Player.log` says so. Without this wait, water names would stay in English for the session and water would stop selling in any other language.
