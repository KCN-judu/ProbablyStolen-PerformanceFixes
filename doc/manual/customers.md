# Customers

## Fast buyer check

**Problem.** When the game decides whether the customer will buy an item (`StoreClient.IsClientBuyingThisItem`), it also checks a container's contents. It lists every item anywhere inside, runs the full check on each one, and each of those lists and checks its own contents again. For nested storage that is quadratic, and every level allocates a new list.

The game runs this check when an item is placed on the counter, when it decides whether something can be sold, and in the negotiation screen, once for every item in the back inventory.

**Fix.** The rule, read from the game's code:

- an item is **bought** when it is acceptable on its own (the game's own check with `ignoreContents`) and, if its contents are checked, everything inside it is bought;
- contents are **checked** unless the item is currently locked, has no content window, or is an advanced gun.

`IsClientBuyingThisItem` and `IsClientRefusingItem`, in the forms that include contents, are patched to evaluate exactly that rule bottom-up, once per item per call. Answers are reused only within that call, never across calls or frames, so the result is always the game's own for the current state. The single-item form runs unchanged.
