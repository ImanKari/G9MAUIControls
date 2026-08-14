namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Handle for bottom sheet operations.
/// </summary>
public interface IG9BottomSheetHandle
{
    /// <summary>Closes the bottom sheet (the owner sheet if the handle is sheet-scoped, otherwise the current).</summary>
    void Close();

    /// <summary>Closes the bottom sheet and resolves once the close animation has finished.</summary>
    Task CloseAsync();

    /// <summary>
    ///     Shows the given content in the current page bottom sheet with the given options.
    ///     Stacks automatically when another sheet is already open.
    /// </summary>
    void Show(View content, G9BottomSheetOptions? options = null);

    /// <summary>Closes the topmost stacked bottom sheet. If no stacked sheets exist, closes the primary sheet.</summary>
    void CloseTop();
}