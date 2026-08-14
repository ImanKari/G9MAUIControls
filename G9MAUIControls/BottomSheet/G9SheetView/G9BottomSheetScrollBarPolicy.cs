using Microsoft.Maui.Handlers;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Hides the scroll bar on every scroller that lives inside an <see cref="G9SheetView" />
///     while leaving scrolling fully functional. Sheet bodies are short and the visible track/thumb
///     reads as visual noise on a floating sheet (see the register-sample form) — content still
///     scrolls, the bar just isn't drawn.
///     <para>
///         <b>Why a handler mapper, not a tree walk or per-XAML flag.</b> A one-time walk of the
///         content tree misses scrollers that are realized LATER — a <c>G9TabView</c> only
///         parents the active tab's content, and deferred/"open then fill" bodies swap in after the
///         open. A handler mapper runs the instant a scroller's platform view is created, whenever
///         that happens, so tab-switched and deferred scrollers are covered with zero timing gaps
///         and no new markup on every sheet.
///     </para>
///     <para>
///         <b>Scope.</b> The mappers are global (they run for every scroller in the app), so each
///         one first walks up the parent chain and only hides the bar when an
///         <see cref="G9SheetView" /> ancestor is found — full-page scrollers keep their normal
///         bars. Setting the MANAGED <c>ScrollBarVisibility</c> property (not the platform view)
///         keeps this cross-platform; MAUI maps it down for us.
///     </para>
/// </summary>
internal static class G9BottomSheetScrollBarPolicy
{
    private const string MappingKey = "G9MAUIControls.G9BottomSheet.HideScrollBars";
    private static bool _registered;

    /// <summary>Idempotent; call once during app startup (from <c>UseG9SheetView</c>).</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        ScrollViewHandler.Mapper.AppendToMapping(MappingKey, static (_, view) =>
        {
            if (view is ScrollView scrollView && IsInsideG9BottomSheet(scrollView))
            {
                scrollView.VerticalScrollBarVisibility = ScrollBarVisibility.Never;
                scrollView.HorizontalScrollBarVisibility = ScrollBarVisibility.Never;
            }
        });

#if ANDROID || IOS || MACCATALYST
        // CollectionView / CarouselView (the list bodies in selection + samples sheets) live in the
        // Handlers.Items namespace on the mobile targets. Windows moved them to a different Items2
        // type, and Windows is a dev-only target here, so it simply keeps its default bars.
        Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping(
            MappingKey, static (_, view) => HideItemsViewBar(view));
        Microsoft.Maui.Controls.Handlers.Items.CarouselViewHandler.Mapper.AppendToMapping(
            MappingKey, static (_, view) => HideItemsViewBar(view));
#endif
    }

#if ANDROID || IOS || MACCATALYST
    private static void HideItemsViewBar(Microsoft.Maui.Controls.ItemsView view)
    {
        if (!IsInsideG9BottomSheet(view))
        {
            return;
        }

        view.VerticalScrollBarVisibility = ScrollBarVisibility.Never;
        view.HorizontalScrollBarVisibility = ScrollBarVisibility.Never;
    }
#endif

    private static bool IsInsideG9BottomSheet(Element? element)
    {
        // Depth-capped so a detached / cyclic tree can never spin. Sheet nesting is shallow: a
        // scroller sits a handful of levels under the G9SheetView host.
        for (var depth = 0; element is not null && depth < 64; depth++)
        {
            if (element is G9SheetView)
            {
                return true;
            }

            element = element.Parent;
        }

        return false;
    }
}
