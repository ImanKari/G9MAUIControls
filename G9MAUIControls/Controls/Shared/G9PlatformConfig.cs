using Microsoft.Maui.Handlers;
#if ANDROID
using AndroidColor = Android.Graphics.Color;
using Android.Content.Res;
using Android.Graphics.Drawables;
using AndroidX.Core.View;
#endif

namespace G9MAUIControls.Controls;

/// <summary>
///     Platform-handler configuration owned by the New-folder control system. Wires up
///     the <c>StyleId="no-underline"</c> opt-in for inner <see cref="Entry" /> / <see cref="Editor" />
///     children of the outlined-field controls (<see cref="G9TextEntry" />,
///     <see cref="G9Editor" />, <c>G9BarcodeTextEntry</c>), and any other
///     platform-side tweak our visual contract relies on.
///     <para>
///         <b>Why it lives here.</b> The mapper logic is part of the new control system —
///         <see cref="G9OutlinedFieldBase" /> sets <c>StyleId = "no-underline"</c> on
///         every Entry / Editor it owns, and the platform strip below is the contract
///         that actually makes the design promise hold (no native underline, no native
///         focus chrome, no hidden content padding around the inner text). Keeping it in
///         <c>Shared</c> means anyone touching the new controls finds the
///         platform tweaks alongside the C# layout / drawable code.
///     </para>
///     <para>
///         <b>Legacy consumers.</b> A handful of pre-existing search bars in the project
///         (TransferContentView, SamplingBatchContentView, FarmsAndGreenhousesContentView,
///         SearchSortFilterHeader, SamplingSamplesListView) also set
///         <c>StyleId="no-underline"</c> directly on plain MAUI <see cref="Entry" />
///         instances. They keep working
///         because the mapper just looks at <c>StyleId</c>; once they migrate to
///         <see cref="G9TextEntry" /> they pick up the same behaviour for free.
///     </para>
/// </summary>
public static class G9PlatformConfig
{
    /// <summary>
    ///     Marker style id. Setting <c>StyleId = NoUnderlineStyleId</c> on any Entry or
    ///     Editor opts the platform handler into stripping native chrome and content
    ///     padding. The new-folder controls set this on every inner Entry / Editor they
    ///     own; legacy callers may set it manually for the same effect.
    /// </summary>
    public const string NoUnderlineStyleId = "no-underline";

    /// <summary>
    ///     Register the Entry / Editor mappers that strip native chrome and content
    ///     padding for opt-in fields. Idempotent — calling more than once is harmless
    ///     because <c>PropertyMapper.AppendToMapping</c>
    ///     keys by the unique mapping name we provide.
    /// </summary>
    public static void Register(IMauiHandlersCollection handlers)
    {
#if WINDOWS
        RegisterWindowsScrollViewFocusFix();
#endif

        EntryHandler.Mapper.AppendToMapping(EntryMappingKey, (handler, view) =>
        {
            if (view is not Entry { StyleId: NoUnderlineStyleId } entry) return;
            if (handler is not EntryHandler entryHandler) return;

            entry.Focused -= OnNoUnderlineEntryFocusChanged;
            entry.Unfocused -= OnNoUnderlineEntryFocusChanged;
            entry.Focused += OnNoUnderlineEntryFocusChanged;
            entry.Unfocused += OnNoUnderlineEntryFocusChanged;

            ApplyNoUnderline(entryHandler);
        });

        EditorHandler.Mapper.AppendToMapping(EditorMappingKey, (handler, view) =>
        {
            if (view is not Editor { StyleId: NoUnderlineStyleId } editor) return;
            if (handler is not EditorHandler editorHandler) return;

            editor.Focused -= OnNoUnderlineEditorFocusChanged;
            editor.Unfocused -= OnNoUnderlineEditorFocusChanged;
            editor.Focused += OnNoUnderlineEditorFocusChanged;
            editor.Unfocused += OnNoUnderlineEditorFocusChanged;

            ApplyNoUnderlineEditor(editorHandler);
        });
    }

    private const string EntryMappingKey = "G9MAUIControls.NoUnderlineEntry";
    private const string EditorMappingKey = "G9MAUIControls.NoUnderlineEditor";

    // ── Focus-event re-application ───────────────────────────────────────────────
    // Some platforms (notably Android) replay the EditText drawable on focus state
    // changes; iOS / macOS Catalyst can re-introduce a thin focus border on the layer.
    // We re-strip on focus events for those. WinUI is intentionally left alone — its
    // chrome strip writes resource-dictionary entries used by the TextBox's visual-state
    // machine, and mutating them while the platform TextBox is draining its own
    // GotFocus / LostFocus cycle has reliably crashed AOT with ExecutionEngineException.
    // The mapper already stripped the chrome on first attach; nothing further is required.

    private static void OnNoUnderlineEntryFocusChanged(object? sender, FocusEventArgs e)
    {
        if (sender is not Entry { StyleId: NoUnderlineStyleId, Handler: EntryHandler handler })
        {
            return;
        }

#if ANDROID
        ApplyNoUnderline(handler);
        handler.PlatformView.Post(() => ApplyNoUnderline(handler));
#elif IOS || MACCATALYST
        ApplyNoUnderline(handler);
#endif
    }

    private static void OnNoUnderlineEditorFocusChanged(object? sender, FocusEventArgs e)
    {
        if (sender is not Editor { StyleId: NoUnderlineStyleId, Handler: EditorHandler editorHandler })
        {
            return;
        }

#if ANDROID
        ApplyNoUnderlineEditor(editorHandler);
        editorHandler.PlatformView.Post(() => ApplyNoUnderlineEditor(editorHandler));
#elif IOS || MACCATALYST
        ApplyNoUnderlineEditor(editorHandler);
#endif
    }

    // ── Per-platform strip ──────────────────────────────────────────────────────

    private static void ApplyNoUnderline(EntryHandler handler)
    {
        try
        {
#if ANDROID
            var platformView = handler.PlatformView;
            var transparent = AndroidColor.Transparent;
            var transparentStateList = ColorStateList.ValueOf(transparent);

            platformView.Background = new ColorDrawable(transparent);
            platformView.SetBackgroundColor(transparent);
            platformView.BackgroundTintList = transparentStateList;
            ViewCompat.SetBackgroundTintList(platformView, transparentStateList);

            // Zero padding — Android Material themes give EditText ~12 px of intrinsic
            // horizontal padding that survives a transparent background and bleeds into
            // our icon-to-text gap. Forcing it to zero makes the visible gap match the
            // explicit InputIconStartMargin / InputIconEndMargin metric.
            platformView.SetPadding(0, 0, 0, 0);
            platformView.CompoundDrawablePadding = 0;
            platformView.Invalidate();
            platformView.RefreshDrawableState();

#elif IOS || MACCATALYST
            if (handler?.PlatformView is not null)
            {
                handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
                handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
                handler.PlatformView.Layer.BorderWidth = 0;
                // BorderStyle=None already removes the rounded-rect / line text inset.
                // Anything larger should be controlled at the host level via metrics.
            }
#elif WINDOWS
            if (handler?.PlatformView is not null)
            {
                ScheduleWinUiTextBoxSetup(handler.PlatformView);
            }
#endif
        }
        catch
        {
            // Swallow — platform handler may not be ready yet on first attach.
        }
    }

    private static void ApplyNoUnderlineEditor(EditorHandler handler)
    {
        try
        {
#if ANDROID
            var platformView = handler.PlatformView;
            var transparent = AndroidColor.Transparent;
            var transparentStateList = ColorStateList.ValueOf(transparent);

            platformView.Background = new ColorDrawable(transparent);
            platformView.SetBackgroundColor(transparent);
            platformView.BackgroundTintList = transparentStateList;
            ViewCompat.SetBackgroundTintList(platformView, transparentStateList);

            // Zero horizontal padding only — keep the platform's vertical padding so
            // multi-line text doesn't visually clip the top / bottom edge of the host.
            platformView.SetPadding(0, platformView.PaddingTop, 0, platformView.PaddingBottom);
            platformView.CompoundDrawablePadding = 0;
            platformView.Invalidate();
            platformView.RefreshDrawableState();

#elif IOS || MACCATALYST
            if (handler?.PlatformView is not null)
            {
                handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
                handler.PlatformView.Layer.BorderWidth = 0;
            }
#elif WINDOWS
            if (handler?.PlatformView is not null)
            {
                // Route the Editor's platform TextBox through the SAME deferred chrome
                // strip as the Entry. EditorHandler.PlatformView is a MauiTextBox : TextBox,
                // so it shares the Entry's border / focus-visual / hidden-padding chrome.
                // The background fill in every visual state (hover / focus / disabled) is
                // neutralized at the WinUI Application scope (Platforms/Windows/App.xaml),
                // and the startup auto-focus / page-jump is handled structurally by the
                // ScrollView IsTabStop fix — neither is done here.
                ScheduleWinUiTextBoxSetup(handler.PlatformView);
            }
#endif
        }
        catch
        {
            // See ApplyNoUnderline — swallow so first attach failures don't crash startup.
        }
    }

#if WINDOWS
    /// <summary>
    ///     Stage the WinUI <see cref="Microsoft.UI.Xaml.Controls.TextBox" /> setup so it
    ///     runs only after the control has been attached to a XAML window.
    ///     <para>
    ///         <b>Why deferred.</b> The mapper invocation that calls this happens during
    ///         <c>EntryHandler.SetVirtualView</c>, when the platform TextBox has been
    ///         instantiated but is NOT yet parented to a <see cref="Microsoft.UI.Xaml.XamlRoot" />.
    ///         Writing <c>tb.Resources["..."] = value</c> at that moment goes through the
    ///         WinRT marshaler, which resolves the dispatcher / window for the resource
    ///         dictionary — finds none — and throws
    ///         <see cref="System.Runtime.InteropServices.COMException" /> HResult
    ///         <c>0x80070580</c> ("Invalid window; it belongs to other thread"). That throw
    ///         was previously swallowed by the outer try/catch. Deferring to
    ///         <see cref="Microsoft.UI.Xaml.FrameworkElement.Loaded" /> guarantees a live
    ///         XamlRoot so the border-brush / focus-underline / hidden-padding resource
    ///         writes succeed. (The background fill in every visual state is neutralized
    ///         separately at the WinUI Application scope — see
    ///         <c>Platforms/Windows/App.xaml</c> and <c>G9Controls.md</c> §15 W10 / W11.)
    ///         See <c>G9Controls.md</c> §15 pitfall <b>W3</b>.
    ///     </para>
    /// </summary>
    private static void ScheduleWinUiTextBoxSetup(Microsoft.UI.Xaml.Controls.Control tb)
    {
        if (tb.IsLoaded)
        {
            StripWinUiTextBoxChrome(tb);
            return;
        }

        Microsoft.UI.Xaml.RoutedEventHandler? handler = null;
        handler = (s, _) =>
        {
            if (s is Microsoft.UI.Xaml.Controls.Control loaded && handler is not null)
            {
                loaded.Loaded -= handler;
            }
            try { StripWinUiTextBoxChrome(tb); }
            catch { /* best-effort cosmetic strip */ }
        };
        tb.Loaded += handler;
    }

    /// <summary>
    ///     Fix the WinUI <c>ScrollViewer</c> behaviour where the first focusable element
    ///     inside a scrollable page (our first <c>TextBox</c> — "Farm name") auto-focuses
    ///     and the page auto-scrolls to it on the first stray click after window
    ///     activation — focusing and scrolling to a field the user never touched.
    ///     <para>
    ///         <b>Root cause.</b> When a WinUI <c>ScrollViewer</c>'s content panel isn't a
    ///         tab-stop, a click that lands on non-focusable chrome (the page background,
    ///         a label, padding) has nowhere to put focus, so the focus manager walks to
    ///         the first focusable descendant and the scroll viewer brings it into view.
    ///         Making the content panel itself a tab-stop gives that click a valid local
    ///         focus target, so focus stays put and nothing scrolls. This is the
    ///         documented fix for the .NET MAUI / WinUI "first editor auto-focus inside a
    ///         ScrollView" issue.
    ///     </para>
    ///     <para>
    ///         It also fixes two downstream symptoms: the field no longer loads in its
    ///         WinUI <c>Focused</c> visual state (so the input doesn't show the focused
    ///         white background fill on a field the user never touched), and a click on
    ///         empty page chrome now moves focus off the active input (tap-outside to
    ///         blur on desktop). Registered once; the mapper is idempotent.
    ///     </para>
    /// </summary>
    private static void RegisterWindowsScrollViewFocusFix()
    {
        Microsoft.Maui.Handlers.ScrollViewHandler.Mapper.AppendToMapping(
            ScrollViewFocusMappingKey,
            (handler, view) =>
            {
                _ = view;
                if (handler.PlatformView is Microsoft.UI.Xaml.Controls.ScrollViewer sv
                    && sv.Content is Microsoft.Maui.Platform.ContentPanel panel)
                {
                    panel.IsTabStop = true;
                }
            });
    }

    private const string ScrollViewFocusMappingKey = "G9MAUIControls.ScrollViewFocusFix";

    /// <summary>
    ///     Strip every visible chrome surface on a WinUI <see cref="Microsoft.UI.Xaml.Controls.Control" />
    ///     (TextBox / RichEditBox) plus the hidden content-area padding so only our
    ///     painted outline is visible and the icon-to-text gap matches the metric exactly.
    ///     <para>
    ///         Each <c>tb.Resources["..."] = ...</c> write is individually try/catched.
    ///         Even after the <see cref="Microsoft.UI.Xaml.FrameworkElement.Loaded" />
    ///         deferral a stray write can still fault if the XamlRoot is mid-teardown; a
    ///         per-write guard keeps the writes that DO succeed from being undone by a
    ///         single failure (and keeps the failure from compounding into the
    ///         debugger-noise / render-thread-destabilising COMException storm described
    ///         in <c>G9Controls.md</c> §15 <b>W3</b>).
    ///     </para>
    /// </summary>
    private static void StripWinUiTextBoxChrome(Microsoft.UI.Xaml.Controls.Control tb)
    {
        var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        TrySet(() => tb.BorderThickness = new Microsoft.UI.Xaml.Thickness(0));
        TrySet(() => tb.Background = transparent);
        TrySet(() => tb.BorderBrush = transparent);
        TrySet(() => tb.UseSystemFocusVisuals = false);
        TrySet(() => tb.FocusVisualPrimaryThickness = new Microsoft.UI.Xaml.Thickness(0));
        TrySet(() => tb.FocusVisualSecondaryThickness = new Microsoft.UI.Xaml.Thickness(0));

        // The XamlRoot may not be attached yet if Loaded fired very early in the
        // activation pipeline. Skip the resource-dictionary writes when no XamlRoot is
        // wired — the next Loaded re-fire catches them. Without this the first Insert
        // goes through the WinRT marshaler with no dispatcher and throws
        // "Invalid window; it belongs to other thread".
        if (tb.XamlRoot is null)
        {
            return;
        }

        var resources = tb.Resources;

        // Border + focus underline at every visual state.
        TrySet(() => resources["TextControlBorderThemeThicknessFocused"] = new Microsoft.UI.Xaml.Thickness(0));
        TrySet(() => resources["TextControlBorderThemeThickness"] = new Microsoft.UI.Xaml.Thickness(0));
        TrySet(() => resources["TextControlBorderBrush"] = transparent);
        TrySet(() => resources["TextControlBorderBrushFocused"] = transparent);
        TrySet(() => resources["TextControlBorderBrushPointerOver"] = transparent);
        TrySet(() => resources["TextControlBorderBrushDisabled"] = transparent);

        // Background at every visual state is neutralized at the WinUI Application scope
        // (Platforms/Windows/App.xaml) rather than here. The default TextBox template
        // swaps BorderElement.Background to {ThemeResource TextControlBackgroundFocused}
        // inside the *Focused* visual-state storyboard, and a per-instance override of
        // that brush does NOT win reliably once the control is templated — the storyboard
        // resolves the ThemeResource against the framework / app dictionaries, not this
        // late instance-scope write. (That's why a per-instance PointerOver override took
        // effect but the Focused one didn't.) The app-level override flattens
        // TextControlBackground / *PointerOver / *Focused / *Disabled for every theme, so
        // no per-instance background write is needed here.

        // The default WinUI theme applies Thickness(12, 6, 6, 6) of padding INSIDE the
        // TextBox content area through the TextControlThemePadding theme resource — that
        // padding survives BorderThickness=0 and Padding=0 because it's baked into the
        // ScrollViewer template part (ContentElement). Without overriding it the inner
        // text appears asymmetrically far from a leading icon. Setting it at the instance
        // scope removes the hidden inset so the icon-to-text gap matches our metric.
        TrySet(() => resources["TextControlThemePadding"] = new Microsoft.UI.Xaml.Thickness(0));

        static void TrySet(Action setter)
        {
            try { setter(); }
            catch { /* swallow — chrome strip is best-effort cosmetic */ }
        }
    }
#endif
}
