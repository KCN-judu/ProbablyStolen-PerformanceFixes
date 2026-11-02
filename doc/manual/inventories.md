# Inventories

## Batch inventory layouts

**Problem.** `GameItem.ValidateShapeState` runs whenever an item enters or leaves an inventory and on every tag change (`EnableTag`, `DisableTag`, `ModifyTag`). After laying out the item, it calls `Validate` on the item's whole inventory, which lays out every item in it again. Adding n items costs n × n layouts.

One save load in a large store ran 161,804 item layouts and 7,000 inventory layouts in a single frame, about 1.8 s. A single tag change on an item in a full storage bay re-lays out the whole bay, and opening a container is one such tag change (`OPENED_TAG`).

**Fix.** While `ValidateShapeState` runs, the inventory layouts it asks for are only recorded. The item's own layout, and everything else, still happens immediately. The recorded inventories are laid out once, just before Unity renders the frame (`Canvas.willRenderCanvases`). What reaches the screen is the same, but repeated requests in one frame cost one layout.

An inventory laid out directly in the meantime is dropped from the list, and an inventory whose UI was destroyed since is skipped.
