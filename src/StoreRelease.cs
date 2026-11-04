using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PerformanceFixes
{
    // Fix 11: leaving the store (back to the main menu, say) lets go of everything that still pointed at it.
    //
    // A store's items, inventories and windows all point at each other, so one reference to any of them kept from a
    // static field keeps the whole store in memory. After leaving it, these references stayed:
    //   - the game's SaveManager keeps the two lookup tables of its last save or load (itemHeap, itemDict) until the
    //     next one starts. Saving on the way out fills them with every item of the store just left. Each save and
    //     load starts with SaveManager.InitHeap, which replaces both, so running it after leaving changes nothing
    //     the game reads;
    //   - the game's list of window roots (GraphHandler.windowGraphNodeRoots): a window leaves it in PixelWindow.Destroy,
    //     but the windows destroyed with the store's scene never go through that, so all of them stayed listed until the
    //     next store's GraphHandler.Awake makes a new list. Only destroyed windows are taken out;
    //   - the game's "current" and "Instance" fields of the store's managers and handlers, which keep pointing at the
    //     destroyed ones until the next store sets them again. Each one is cleared only while it points at a destroyed
    //     object, which Unity already treats as null, so every check the game makes on them answers the same;
    //   - a scratch list of item UI nodes (GameItemElement.innerNodeBuffer);
    //   - this mod's own caches (selection slots and shapes, waiting layouts and preset windows, kept window homes).
    // Each load in the same session then started with the previous store still in memory, and loads got slower as
    // the managed heap grew. When a scene other than the store replaces it, these are released and one collection
    // runs while the menu is idle. Player.log gets one line with the managed memory before and after.
    static class StoreRelease
    {
        public static bool Enabled = true;

        // The store's scene name, as in code_standard.md; the main menu is EmporiumMenu.
        const string StoreScene = "InventoryScene";

        // SaveManager.InitHeap is private and called by reflection. inStore is true once the store scene has been loaded,
        // so the first other scene afterwards is the one that follows leaving it.
        static MethodInfo initHeap;
        static bool inStore;

        // No Harmony patch: this fix only listens to sceneLoaded, so Unpatch must unsubscribe.
        public static void Patch(Harmony harmony)
        {
            initHeap = typeof(SaveManager).GetMethod("InitHeap", PerformanceMod.AnyStatic, null, Type.EmptyTypes, null);
            if (initHeap == null) throw new MissingMethodException("SaveManager.InitHeap");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public static void Unpatch() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        // Single mode only: an additive scene is not leaving the store.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (scene.name == StoreScene) { inStore = true; return; }
            if (!inStore) return;
            inStore = false;
            if (Enabled) Release(scene.name);
        }

        // Each step runs on its own, so one failing step does not keep the others from releasing. The forced collection
        // is what makes the next load start from a clean heap; the line gives the managed memory before and after.
        static void Release(string sceneName)
        {
            long before = GC.GetTotalMemory(false);
            Try(() => initHeap.Invoke(null, null));
            Try(DropDestroyedWindows);
            Try(DropDestroyedManagers);
            Try(() => ClearList(typeof(GameItemElement).GetField("innerNodeBuffer", PerformanceMod.AnyStatic)));
            Try(SelectionHighlight.Forget);
            Try(InventoryBatching.Forget);
            Try(StaggeredPresets.Forget);
            Try(KeepAdopted.Forget);
            Try(FastHide.Forget);
            GC.Collect();
            long after = GC.GetTotalMemory(true);
            Debug.Log("[PerformanceFixes] left the store for '" + sceneName + "': released " + windows + " window(s) and " + managers +
                " manager(s) that still pointed at it; managed memory " + Mb(before) + " -> " + Mb(after));
        }

        // Counts for the log line, set by the two Drop steps.
        static int windows, managers;

        // The window roots of GraphHandler are plain objects, not Unity ones, so a destroyed window is recognised by its
        // handler being gone.
        static void DropDestroyedWindows()
        {
            windows = 0;
            var roots = GraphHandler.windowGraphNodeRoots;
            if (roots == null) return;
            windows = roots.RemoveAll(w => { var p = w as PixelWindow; return p != null && p.handler == null; });
        }

        // Every game MonoBehaviour's static field of its own type (current, Instance and the like), listed from the game's
        // code. Only those still pointing at a destroyed object are cleared.
        static void DropDestroyedManagers()
        {
            managers = 0;
            Drop(typeof(AchievementTracker).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(AdvCalendarUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(AudioDirectory).GetField("activeDirectory", PerformanceMod.AnyStatic));
            Drop(typeof(AudioManager).GetField("mInstance", PerformanceMod.AnyStatic));
            Drop(typeof(BargainUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(ChemFinisherUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(ChemScannerUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(ClienteleUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(CommissaryUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(CustomUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(DebugHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(DebugOwnershipToggleMode).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(DebugPanel).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(DialogSettingUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(DialogUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(DialogueHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(DialoguePhoneUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(EmporiumAudioPlayer).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(EmporiumEntry).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(EmporiumOption).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(EmporiumRTSController).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(EndOfDayUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(EventTriggerManager).GetField("<current>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(ExchangeUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(FontLoader).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(GameMaster).GetField("<current>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(GameOptionUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(GameOverUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(GenericConfirmationUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(HandnoteUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(IconProvider).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(InputActionManager).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(InspectionUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(Interactable).GetField("currentHoveringInteractable", PerformanceMod.AnyStatic));
            Drop(typeof(InteractableUI).GetField("currentHoveringInteractable", PerformanceMod.AnyStatic));
            Drop(typeof(ItemBehaviourManager).GetField("<current>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(ItemContextHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemDefaultActivateHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemMouseDoubleClickHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemMouseDragHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemMouseHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemMultiSelectHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemQuickTransferAltHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemQuickTransferAltHandler1).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemQuickTransferHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemSelectHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(ItemTooltipHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(LabelerBlueUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(LabelerUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(LetterboxManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(LoanPanelUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(LogUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(LogWriter).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(MainMenuUIController).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(MapUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(MultiPageUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(NegociationUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(NewGameData).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(NewsUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(NotebookUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(OverlayHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(PCUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(PaperEditor).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(PaperUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(PerkUIController).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(PhoneUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(PlantAnalyserUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(QuickChooseHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(RecipeSelectHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(RecordUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(RecorderUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(RenderHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(RepublicLogoUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(ReputationUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(ScratcherUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(SelectableElementHandler).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(SpriteDict).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StallWatchdog).GetField("instance", PerformanceMod.AnyStatic));
            Drop(typeof(StartOfDayUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StoreClientMono).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StorePoster).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StoreScale).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StoreSecButton).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StoreShutterButton).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StoreSoundPlayer).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StoreUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StoreVendingFountain).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(StoreVendingMachine).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(TableGroupMaster).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(TableMaster).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(TooltipManager).GetField("_instance", PerformanceMod.AnyStatic));
            Drop(typeof(TutorialUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(UIDragHandler).GetField("currentDragger", PerformanceMod.AnyStatic));
            Drop(typeof(UISettings).GetField("current", PerformanceMod.AnyStatic));
            Drop(typeof(WalkerManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(WantedUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(WaterScannerUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(WaterStripUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(WildUIManager).GetField("<Instance>k__BackingField", PerformanceMod.AnyStatic));
            Drop(typeof(WindowsHandler).GetField("current", PerformanceMod.AnyStatic));
        }

        // Clears the static field only if it holds a destroyed Unity object (== null is true, ReferenceEquals is not).
        // A live object, as in a menu scene that keeps one, is left alone.
        static void Drop(FieldInfo field)
        {
            if (field == null || !field.IsStatic) return;
            var value = field.GetValue(null) as UnityEngine.Object;
            if (ReferenceEquals(value, null) || value != null) return;
            field.SetValue(null, null);
            managers++;
        }

        // Empties a static list the game fills but never clears.
        static void ClearList(FieldInfo field)
        {
            var list = field == null ? null : field.GetValue(null) as System.Collections.IList;
            if (list != null) list.Clear();
        }

        // Isolates one release step; a failure is logged and never reaches the game.
        static void Try(Action step)
        {
            try { step(); }
            catch (Exception e) { Debug.LogWarning("[PerformanceFixes] releasing the store: " + e.GetType().Name + ": " + e.Message); }
        }

        static string Mb(long bytes) { return (bytes / (1024.0 * 1024.0)).ToString("0") + " MB"; }
    }
}
