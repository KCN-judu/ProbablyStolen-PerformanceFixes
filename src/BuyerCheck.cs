using System.Collections.Generic;
using HarmonyLib;

namespace PerformanceFixes
{
    // Fix 1: makes the game's "would this customer buy it" check linear instead of quadratic.
    //
    // StoreClient.IsClientBuyingThisItem(item) on a container lists every item inside it (GraphUtils.
    // FindAllChildrenType) and calls itself on each one, and each of those does the same for its own contents. In a
    // deep store that is quadratic, with a fresh list at every level, and the game runs it when an item is placed on
    // the counter, when it checks whether something can be sold, and in the negotiation screen (once per item in the
    // back inventory).
    //
    // The rule itself, read from the game:
    //   bought(x)    = acceptable(x) and (contents not checked(x) or everything inside x is bought)
    //   acceptable(x) = IsClientBuyingThisItem(x, ignoreContents: true)
    //   checked(x)   = not LockHelper.IsCurrentlyLocked(x) and x has a content window and x is not an ADV_GUN
    // This patch evaluates exactly that bottom-up, once per item per call. Answers are reused only within one call,
    // so the result is always the game's own for the current state.
    static class BuyerCheck
    {
        const int MaxDepth = 32;

        // Per call: bought(item) and "everything inside item is bought".
        static readonly Dictionary<GameItem, bool> bought = new Dictionary<GameItem, bool>();
        static readonly Dictionary<GameItem, bool> allInside = new Dictionary<GameItem, bool>();
        static readonly HashSet<GraphNodeStorage> nodesSeen = new HashSet<GraphNodeStorage>();
        static StoreClient client;
        static bool busy;

        public static bool Enabled = true;

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(typeof(StoreClient).GetMethod("IsClientBuyingThisItem", PerformanceMod.AnyInstance, null,
                    new[] { typeof(GameItem), typeof(bool) }, null),
                new HarmonyMethod(typeof(BuyerCheck).GetMethod("BuyingPrefix", PerformanceMod.AnyStatic)));
            harmony.Patch(typeof(StoreClient).GetMethod("IsClientRefusingItem", PerformanceMod.AnyInstance, null,
                    new[] { typeof(GameItem), typeof(bool) }, null),
                new HarmonyMethod(typeof(BuyerCheck).GetMethod("RefusingPrefix", PerformanceMod.AnyStatic)));
        }

        // The single-item form (ignoreContents) is cheap and runs unchanged; so does a call made while this patch is
        // already evaluating (it only ever asks for the single-item form).
        static bool BuyingPrefix(StoreClient __instance, GameItem gameItem, bool ignoreContents, ref bool __result)
        {
            if (!Enabled || ignoreContents || gameItem == null || busy) return true;
            busy = true;
            client = __instance;
            try { __result = Bought(gameItem, 0); }
            finally
            {
                bought.Clear(); allInside.Clear(); nodesSeen.Clear();
                client = null;
                busy = false;
            }
            return false;
        }

        // The negotiation screen asks the refusal side directly:
        //   refusing(x) = refusing on its own (ignoreContents) or (contents checked(x) and something inside is not bought)
        static bool RefusingPrefix(StoreClient __instance, GameItem gameItem, bool ignoreContents, ref bool __result)
        {
            if (!Enabled || ignoreContents || gameItem == null || busy) return true;
            busy = true;
            client = __instance;
            try
            {
                __result = __instance.IsClientRefusingItem(gameItem, true) ||
                    (ContentsChecked(gameItem) && !AllInsideBought(gameItem, 0));
            }
            finally
            {
                bought.Clear(); allInside.Clear(); nodesSeen.Clear();
                client = null;
                busy = false;
            }
            return false;
        }

        static bool Bought(GameItem item, int depth)
        {
            bool result;
            if (bought.TryGetValue(item, out result)) return result;
            result = client.IsClientBuyingThisItem(item, true) &&
                (!ContentsChecked(item) || AllInsideBought(item, depth));
            bought[item] = result;
            return result;
        }

        static bool ContentsChecked(GameItem item)
        {
            return !LockHelper.IsCurrentlyLocked(item) && item.contentWindow != null && !item.IsTag("ADV_GUN_TAG");
        }

        // Every item anywhere inside, through inventories and other nodes, as GraphUtils.FindAllChildrenType lists
        // them; each item visited once.
        static bool AllInsideBought(GameItem item, int depth)
        {
            bool result;
            if (allInside.TryGetValue(item, out result)) return result;
            result = depth >= MaxDepth || NodesBought(item.children, depth + 1);
            allInside[item] = result;
            return result;
        }

        static bool NodesBought(List<GraphNodeStorage> nodes, int depth)
        {
            if (nodes == null) return true;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                var item = node as GameItem;
                if (item != null)
                {
                    if (!Bought(item, depth) || !AllInsideBought(item, depth)) return false;
                }
                else if (depth < MaxDepth && nodesSeen.Add(node) && !NodesBought(node.children, depth + 1))
                    return false;
            }
            return true;
        }
    }
}
