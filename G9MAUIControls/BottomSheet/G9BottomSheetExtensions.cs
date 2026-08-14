namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Extension methods for page-level bottom sheet access.
/// </summary>
public static class G9BottomSheetExtensions
{
    extension(VisualElement parentElement)
    {
        /// <summary>
        ///     Returns a handle to the current-page bottom sheet so callers can close before showing
        ///     or control it from content.
        /// </summary>
        public IG9BottomSheetHandle InitG9BottomSheet()
        {
            return G9BottomSheetHelper.InitG9BottomSheet();
        }

        /// <summary>
        ///     Shows content in the bottom sheet of the current visible page.
        /// </summary>
        public void ShowGlobalG9BottomSheet(
            View content,
            G9BottomSheetOptions? options = null)
        {
            G9BottomSheetHelper.ShowG9BottomSheet(content, options);
        }

        /// <summary>Shows a full-screen bottom sheet and returns a handle to control it.</summary>
        public Task<IG9BottomSheetHandle> ShowFullScreenG9BottomSheetAsync(
            View content,
            G9BottomSheetOptions? options = null)
        {
            return G9BottomSheetHelper.ShowFullScreenAsync(parentElement, content, options);
        }

        /// <summary>Shows a lazily-created full-screen bottom sheet and returns a handle to control it.</summary>
        public Task<IG9BottomSheetHandle> ShowFullScreenG9BottomSheetAsync(
            Func<View> contentFactory,
            G9BottomSheetOptions? options = null,
            Action<View>? onContentCreated = null)
        {
            return G9BottomSheetHelper.ShowFullScreenAsync(
                parentElement,
                contentFactory,
                options,
                onContentCreated);
        }

        /// <summary>
        ///     Closes the current visible page bottom sheet.
        /// </summary>
        public void CloseGlobalG9BottomSheet()
        {
            G9BottomSheetHelper.CloseG9BottomSheet();
        }

        /// <summary>
        ///     Shows a reusable selectable list picker in a bottom sheet and returns selected item(s) on close.
        /// </summary>
        public Task<IReadOnlyList<G9BottomSheetListItem>> ShowListG9BottomSheetAsync(
            string title,
            IEnumerable<G9BottomSheetListItem> items,
            IEnumerable<G9BottomSheetListItem>? selectedItems = null,
            bool allowMultipleSelection = false,
            bool closeOnSingleSelection = true,
            G9BottomSheetOptions? options = null)
        {
            return G9BottomSheetHelper.ShowListG9BottomSheetAsync(
                title,
                items,
                selectedItems,
                allowMultipleSelection,
                closeOnSingleSelection,
                options);
        }

        /// <summary>
        ///     Closes the topmost stacked bottom sheet. If no stacked sheets exist, closes the primary sheet.
        /// </summary>
        public void CloseTopG9BottomSheet()
        {
            G9BottomSheetHelper.CloseTopG9BottomSheet();
        }
    }
}
