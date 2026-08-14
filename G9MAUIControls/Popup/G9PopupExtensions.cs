// G9Popup-flavored fluent extensions on VisualElement so callers can write
// `await this.ShowG9PopupAsync(...)` instead of `await G9PopupHelper.ShowG9PopupAsync(...)`.
// Namespace follows the folder path (Components.G9Popup). Toast-flavored extensions live
// separately in the Toast component folder (`Toast/G9ToastExtensions.cs`).
namespace G9MAUIControls.Popup;

/// <summary>
///     Fluent <see cref="VisualElement" /> extensions that forward to <c>G9PopupHelper</c>.
/// </summary>
public static class G9PopupExtensions
{
    extension(VisualElement parentElement)
    {
        /// <summary>Shows an information popup. Usage: await this.ShowG9PopupAsync("Message", "Title").</summary>
        public Task<G9PopupResult> ShowG9PopupAsync(string message,
            string? title = null,
            IEnumerable<G9PopupButton>? buttons = null,
            G9PopupSettings? settings = null,
            G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
        {
            return G9PopupHelper.ShowG9PopupAsync(message, title, buttons, settings, animation);
        }

        /// <summary>Shows a success popup.</summary>
        public Task<G9PopupResult> ShowSuccessG9PopupAsync(string message,
            string? title = null,
            IEnumerable<G9PopupButton>? buttons = null,
            G9PopupSettings? settings = null,
            G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
        {
            return G9PopupHelper.ShowSuccessG9PopupAsync(message, title, buttons, settings, animation);
        }

        /// <summary>Shows a warning popup.</summary>
        public Task<G9PopupResult> ShowWarningG9PopupAsync(string message,
            string? title = null,
            IEnumerable<G9PopupButton>? buttons = null,
            G9PopupSettings? settings = null,
            G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
        {
            return G9PopupHelper.ShowWarningG9PopupAsync(message, title, buttons, settings, animation);
        }

        /// <summary>Shows an error popup.</summary>
        public Task<G9PopupResult> ShowErrorG9PopupAsync(string message,
            string? title = null,
            IEnumerable<G9PopupButton>? buttons = null,
            G9PopupSettings? settings = null,
            G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
        {
            return G9PopupHelper.ShowErrorG9PopupAsync(message, title, buttons, settings, animation);
        }

        /// <summary>Shows a custom view in a popup.</summary>
        public Task<G9PopupResult> ShowCustomG9PopupAsync(View view,
            string? title = null,
            IEnumerable<G9PopupButton>? buttons = null,
            G9PopupSettings? settings = null,
            G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
        {
            return G9PopupHelper.ShowCustomG9PopupAsync(view, title, buttons, settings, animation);
        }

        /// <summary>Shows an input popup with one or more fields and returns entered values.</summary>
        public Task<G9PopupInputResult> ShowInputG9PopupAsync(G9PopupInputOptions options)
        {
            return G9PopupHelper.ShowInputG9PopupAsync(options);
        }
    }
}
