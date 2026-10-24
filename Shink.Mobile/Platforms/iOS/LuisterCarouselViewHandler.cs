using Microsoft.Maui.Controls.Handlers.Items;
using Shink.Mobile.Pages;
using UIKit;

namespace Shink.Mobile.Platforms.iOS;

// A spacer header is represented by ContentInset and a negative ContentOffset
// in MAUI's legacy iOS handler. UIKit can reset that offset when a parent feed
// scrolls/recycles. Put the gutter in the item layout instead, where offset zero
// still leaves the first card inset. Keep the existing stable cell handler.
internal sealed class LuisterCarouselViewHandler : CollectionViewHandler
{
    protected override ItemsViewController<ReorderableItemsView> CreateController(
        ReorderableItemsView itemsView, ItemsViewLayout layout) => new CarouselController(itemsView, layout);

    private sealed class CarouselController(ReorderableItemsView itemsView, ItemsViewLayout layout)
        : ReorderableItemsViewController<ReorderableItemsView>(itemsView, layout)
    {
        protected override UICollectionViewDelegateFlowLayout CreateDelegator() =>
            new CarouselDelegator(ItemsViewLayout, this, (ItemsView as LuisterCarouselView)?.EdgeInset ?? 0);
    }

    // The stock groupable delegate returns zero for ungrouped collections,
    // ignoring ListViewLayout.GetInsetForSection. Supply the inset here while
    // retaining the stock selection, recycling and scroll delegate behavior.
    private sealed class CarouselDelegator(
        ItemsViewLayout itemsViewLayout, CarouselController controller, double edgeInset)
        : ReorderableItemsViewDelegator<ReorderableItemsView, CarouselController>(itemsViewLayout, controller)
    {
        public override UIEdgeInsets GetInsetForSection(
            UICollectionView collectionView, UICollectionViewLayout layout, nint section)
        {
            var inset = base.GetInsetForSection(collectionView, layout, section);
            if (section == 0) inset.Left += (nfloat)edgeInset;
            if (section == collectionView.NumberOfSections() - 1) inset.Right += (nfloat)edgeInset;
            return inset;
        }
    }
}
