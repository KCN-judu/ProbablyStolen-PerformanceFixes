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
    // The rule itself, read from the game, is evaluated bottom-up once per item per call by BuyerRule (its own file,
    // with tests); this patch only supplies the game's answers for single items:
    //   acceptable(x) = IsClientBuyingThisItem(x, ignoreContents: true)
    //   checked(x)    = not LockHelper.IsCurrentlyLocked(x) and x has a content window and x is not an ADV_GUN
    // Answers are reused only within one call, so the result is always the game's own for the current state. When the
    // contents nest deeper than BuyerRule.MaxDepth, the rule gives no answer and the game's own method runs for that
    // call; its checks of the items inside come back through this patch, one level down each time.
    static class BuyerCheck
    {
        static StoreClient client;
        static bool busy;

        static readonly BuyerRule<GraphNodeStorage> rule = new BuyerRule<GraphNodeStorage>(
            node => node is GameItem,
            node => client.IsClientBuyingThisItem((GameItem)node, true),
            node => ContentsChecked((GameItem)node),
            node => node.children);

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
            bool? answer;
            busy = true;
            client = __instance;
            try { answer = rule.Buying(gameItem); }
            finally { client = null; busy = false; }
            if (answer == null) return true;
            __result = answer.Value;
            return false;
        }

        // The negotiation screen asks the refusal side directly.
        static bool RefusingPrefix(StoreClient __instance, GameItem gameItem, bool ignoreContents, ref bool __result)
        {
            if (!Enabled || ignoreContents || gameItem == null || busy) return true;
            bool? answer;
            busy = true;
            client = __instance;
            try { answer = rule.Refusing(gameItem, __instance.IsClientRefusingItem(gameItem, true)); }
            finally { client = null; busy = false; }
            if (answer == null) return true;
            __result = answer.Value;
            return false;
        }

        static bool ContentsChecked(GameItem item)
        {
            return !LockHelper.IsCurrentlyLocked(item) && item.contentWindow != null && !item.IsTag("ADV_GUN_TAG");
        }
    }
}
