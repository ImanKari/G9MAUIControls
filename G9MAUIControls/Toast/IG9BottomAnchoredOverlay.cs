namespace G9MAUIControls.Toast;

/// <summary>
///     Marks a long-lived, bottom-anchored overlay of your own that toasts should stack
///     <b>above</b> instead of covering.
///     <para>
///         Toasts default to the bottom corner, which is exactly where a persistent status surface
///         tends to live — a sync-progress card, a media mini-bar, an offline banner. Without this
///         marker the next toast simply appears on top of it, and whatever the user was reading
///         disappears behind a message that will auto-dismiss in three seconds.
///     </para>
///     <para>
///         Implement it on the overlay view and mount that view in the same host the toasts use
///         (<c>ToastHost</c>). <see cref="G9ToastHelper" /> then measures your overlay and lifts the
///         bottom-anchored toast stack clear of it, re-flowing on every show, dismiss and size
///         change. Nothing else is required — there are no members to write.
///     </para>
///     <example>
///         <code>
///         public sealed class SyncStatusCard : ContentView, IG9BottomAnchoredOverlay
///         {
///             public SyncStatusCard() => VerticalOptions = LayoutOptions.End;
///         }
///         </code>
///     </example>
///     <para>
///         Only views whose <see cref="Microsoft.Maui.Controls.VisualElement.IsVisible" /> is true
///         and whose vertical alignment is <see cref="Microsoft.Maui.Controls.LayoutOptions.End" />
///         participate, so hiding the overlay drops the toasts back down on its own.
///     </para>
/// </summary>
public interface IG9BottomAnchoredOverlay;
