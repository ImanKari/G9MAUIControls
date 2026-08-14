// Toast-flavored fluent extensions on VisualElement so callers can write
// `await this.ShowToastAsync(...)` instead of `await G9ToastHelper.ShowToastAsync(...)`.
// Namespace follows the folder path (Components.Toast). G9Popup-flavored extensions live
// separately in the G9Popup component folder (`G9Popup/G9PopupExtensions.cs`).
namespace G9MAUIControls.Toast;

/// <summary>
///     Fluent <see cref="VisualElement" /> extensions that forward to <see cref="G9ToastHelper" />.
/// </summary>
public static class G9ToastExtensions
{
    extension(VisualElement parentElement)
    {
        /// <summary>Shows a typed, auto-dismissing toast with icon and optional action button.</summary>
        public Task ShowToastAsync(
            string message,
            G9ToastType type = G9ToastType.Information,
            G9ToastOptions? options = null)
        {
            return G9ToastHelper.ShowToastAsync(message, type, options);
        }

        /// <summary>Dismisses the active toast.</summary>
        public Task DismissToastAsync()
        {
            return G9ToastHelper.DismissToastAsync();
        }

        /// <summary>Shows a full-screen loading overlay with busy indicator.</summary>
        public Task ShowLoadingAsync(string text)
        {
            return G9ToastHelper.ShowLoadingAsync(text);
        }

        /// <summary>Dismisses the full-screen loading overlay.</summary>
        public Task DismissLoadingAsync()
        {
            return G9ToastHelper.DismissLoadingAsync();
        }

        /// <summary>Shows a compact, positioned loading toast.</summary>
        public Task ShowLoadingToastAsync(string text, G9ToastPosition? position = null)
        {
            return G9ToastHelper.ShowLoadingToastAsync(text, position);
        }

        /// <summary>Dismisses the compact loading toast.</summary>
        public Task DismissLoadingToastAsync()
        {
            return G9ToastHelper.DismissLoadingToastAsync();
        }
    }
}
