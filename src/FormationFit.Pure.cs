using System.Collections.Generic;

namespace PerformanceFixes
{
    // When the "does the whole group fit" check of a group drag must run again. Uses only System types, so
    // tests/FormationFitTest.cs runs it outside the game.
    //
    // The answer depends on the inventory, the drop offset, the selected items (in order) and the contents of every
    // inventory, which the version counts: any item that enters or leaves an inventory, moves or changes a tag bumps
    // it. While all of them are as they were, the last answer still holds.
    sealed class FormationFit<TInventory, TItem> where TInventory : class where TItem : class
    {
        readonly List<TItem> selection = new List<TItem>();
        TInventory inventory;
        int deltaX, deltaY, version;
        bool known, fits;

        public bool TryGet(TInventory inventory, int deltaX, int deltaY, int version, List<TItem> items, out bool fits)
        {
            fits = this.fits;
            return known && this.inventory == inventory && this.deltaX == deltaX && this.deltaY == deltaY &&
                this.version == version && Same(items);
        }

        public void Store(TInventory inventory, int deltaX, int deltaY, int version, List<TItem> items, bool fits)
        {
            this.inventory = inventory;
            this.deltaX = deltaX;
            this.deltaY = deltaY;
            this.version = version;
            selection.Clear();
            selection.AddRange(items);
            this.fits = fits;
            known = true;
        }

        public void Forget()
        {
            known = false;
            inventory = null;
            selection.Clear();
        }

        bool Same(List<TItem> items)
        {
            if (items.Count != selection.Count) return false;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != selection[i]) return false;
            return true;
        }
    }
}
