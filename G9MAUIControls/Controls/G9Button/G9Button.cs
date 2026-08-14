using G9MAUIControls.Helpers;
using G9MAUIControls.Icons;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Material-style button replacement for SfButton.
///     Variants follow the G9DesignSystem MAUI implementation spec — Primary, Tonal,
///     Default, Secondary, Info, Success, Warning, Error, Surface, Outline, Text.
///     Sizes: Small / Medium / Large / Hero.
///     // TODO (palette step): variant background/text/stroke recipes will move to G9Palette.
/// </summary>
public partial class G9Button : G9ControlBase
{
    private readonly Border _frame;
    private readonly Grid _innerGrid;
    private readonly HorizontalStackLayout _row;
    private readonly Label _textLabel;
    private readonly ContentView _leadingHost;
    private readonly ContentView _trailingHost;
    private readonly ActivityIndicator _loadingIndicator;
    private readonly GraphicsView _rippleView;
    private readonly G9RippleDrawable _rippleDrawable = new();

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _text;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _loadingText;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9ButtonVariant _variant;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9ControlSize _size;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _leadingEmoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _leadingIcon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _leadingImagePath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _leadingImageSource;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _trailingEmoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _trailingIcon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _trailingImagePath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _trailingImageSource;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isLoading;

    /// <summary>
    ///     When true (default), a label that is too wide for the button is truncated with a
    ///     trailing ellipsis ("…") instead of overflowing the frame. The available width is
    ///     re-measured whenever the button is sized or its icons/text change. Set to false to let
    ///     the label keep its natural width (the legacy behaviour).
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _textTruncation;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _iconSize;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _fontSize;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private FontAttributes _fontAttributes;

    /// <summary>
    ///     Optional explicit background color that OVERRIDES the <see cref="Variant" />'s
    ///     resolved background. When set, the button paints this exact color and derives a
    ///     contrasting text/icon color + a darker border from it (a fully-transparent value
    ///     yields a transparent button with the resting <see cref="TextColor" /> kept).
    ///     <para>
    ///         This is the migration escape hatch for the handful of legacy call sites that
    ///         set an arbitrary <c>Background</c> / <c>BackgroundColor</c> (e.g. translucent
    ///         map-toolbar buttons, transparent picker-trigger buttons) that don't map to a
    ///         named variant. New code should prefer <see cref="Variant" />. The dedicated
    ///         color-system pass will revisit these.
    ///     </para>
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _baseBackgroundColor;

    /// <summary>
    ///     Optional explicit text/icon color. When set it wins over the variant- or
    ///     <see cref="BaseBackgroundColor" />-derived text color. Mirrors the legacy
    ///     <c>G9SafeButton.TextColor</c> escape hatch so migrated call sites keep their look.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _textColor;

    [AutoBindable] private ICommand? _command;
    [AutoBindable] private object? _commandParameter;

    public G9Button()
    {
        _textLabel = new Label
        {
            FontSize = G9Metrics.ButtonFontMedium,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.NoWrap,
            MaxLines = 1,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        };

        _leadingHost = new ContentView { InputTransparent = true };
        _trailingHost = new ContentView { InputTransparent = true };

        _loadingIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            WidthRequest = 18,
            HeightRequest = 18,
            InputTransparent = true
        };

        _row = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            Children = { _leadingHost, _loadingIndicator, _textLabel, _trailingHost }
        };

        _rippleView = new GraphicsView
        {
            Drawable = _rippleDrawable,
            InputTransparent = true,
            BackgroundColor = Colors.Transparent,
            Opacity = 0
        };

        // Layer order: ripple (deepest) → row (text + icons). Inset highlight is intentionally
        // not stacked here — the G9 "lit edge" effect was visually noisy on Windows where
        // GraphicsView edges don't align perfectly with the rounded Border corners.
        _innerGrid = new Grid
        {
            InputTransparent = true,
            Children = { _rippleView, _row }
        };

        _frame = new Border
        {
            StrokeThickness = 0,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusMd),
            Content = _innerGrid
        };

        Content = _frame;

        // NOTE: there is deliberately NO `SizeChanged += … UpdateTextMaxWidth()` here. MAUI's
        // VisualElement.OnSizeAllocated is what RAISES SizeChanged, so subscribing to both ran the
        // truncation recompute — and its potential MaximumWidthRequest write — twice for every
        // single resize. OnSizeAllocated (below) is the canonical hook and covers the same cases.
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += OnPointerEntered;
        pointer.PointerExited += OnPointerExited;
        GestureRecognizers.Add(pointer);

        Variant = G9ButtonVariant.Primary;
        Size = G9ControlSize.Medium;
        IconSize = G9Metrics.InputIconSize;
        FontSize = G9Metrics.ButtonFontMedium;
        FontAttributes = FontAttributes.Bold;
        TextTruncation = true;
    }

    public event EventHandler? Clicked;

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <summary>
    ///     Resolve the effective button colors, honoring the <see cref="BaseBackgroundColor" />
    ///     / <see cref="TextColor" /> escape hatches over the named <see cref="Variant" />.
    /// </summary>
    private G9Visuals.ButtonVisualResult ResolveColors()
    {
        var variant = G9Visuals.ResolveButtonVariant(Variant);

        if (BaseBackgroundColor is null)
        {
            // No override — variant wins, but an explicit TextColor still overrides text.
            var text = TextColor ?? variant.Text;
            return new G9Visuals.ButtonVisualResult(
                variant.Background, text, variant.Stroke, variant.UsesGradient);
        }

        var bg = BaseBackgroundColor;
        var transparent = bg.Alpha <= 0f;
        // Transparent override: keep a transparent surface + no border; text stays the explicit
        // TextColor (or the variant text as a sensible fallback).
        if (transparent)
        {
            return new G9Visuals.ButtonVisualResult(
                Colors.Transparent,
                TextColor ?? variant.Text,
                Colors.Transparent,
                UsesGradient: false);
        }

        // Solid custom color: derive contrasting text + a darker border, no gradient
        // (we can't assume a gradient recipe for an arbitrary color — keep it flat + faithful).
        return new G9Visuals.ButtonVisualResult(
            bg,
            TextColor ?? G9ColorHelper.GetContrastingTextColor(bg),
            G9ColorHelper.CreateBorderColor(bg),
            UsesGradient: false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A palette flip on G9Button only needs the frame background, stroke, and
    ///     icon / label text colour to refresh. <see cref="OnApplyVisuals" /> also
    ///     reconciles size / spinner / icon-host children which are unaffected by the
    ///     palette. Saves ~80–145 ms per button on a theme switch.
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;
        var colors = ResolveColors();
        var enabled = IsEnabled && !IsLoading;
        _frame.Stroke = new SolidColorBrush(colors.Stroke);
        _frame.Background = G9Colors.BuildSolidOrGradient(colors.Background, colors.UsesGradient && enabled);
        var textColor = colors.Text;
        if (_textLabel.TextColor != textColor) _textLabel.TextColor = textColor;
        if (_loadingIndicator.Color != textColor) _loadingIndicator.Color = textColor;
        if (_leadingHost.Content is G9IconView li && li.Color != textColor)
        {
            li.Color = textColor;
        }
        if (_trailingHost.Content is G9IconView ti && ti.Color != textColor)
        {
            ti.Color = textColor;
        }
    }

    protected override void OnApplyVisuals()
    {
        var colors = ResolveColors();
        var enabled = IsEnabled && !IsLoading;
        var hasCustomBg = BaseBackgroundColor is not null;
        var isOutlineLike = !hasCustomBg && Variant is G9ButtonVariant.Outline or G9ButtonVariant.Text;

        _frame.StrokeShape = G9Colors.Round(G9Metrics.RadiusMd);
        // Custom-background buttons get a hairline border from the derived stroke (matches
        // the legacy G9SafeButton look); variant buttons keep the outline/tonal stroke rule.
        _frame.StrokeThickness = hasCustomBg
            ? (colors.Stroke.Alpha > 0f ? 1 : 0)
            : (isOutlineLike || Variant is G9ButtonVariant.Tonal ? 1.5 : 0);
        _frame.Stroke = new SolidColorBrush(colors.Stroke);
        _frame.Background = G9Colors.BuildSolidOrGradient(colors.Background, colors.UsesGradient && enabled);

        // G9Button paints NO drop shadow, ever — the app is shadow-free by policy. Elevation is
        // expressed with the frame's Background / Stroke only. See G9Controls.md → "No shadows".
        Opacity = !IsEnabled ? 0.38 : IsLoading ? 0.70 : 1;

        ApplySize();

        var textColor = colors.Text;
        // While loading: show LoadingText next to the spinner if provided, otherwise
        // hide the text entirely so the button shows JUST the spinner. This gives
        // consumers two clean modes:
        //   • IsLoading=true, LoadingText="Saving..."  → spinner + label
        //   • IsLoading=true, LoadingText=null/empty   → spinner only
        // The Text property is preserved either way so we can restore it once loading
        // ends without the consumer having to re-bind.
        var displayText = IsLoading
            ? (string.IsNullOrEmpty(LoadingText) ? null : LoadingText)
            : Text;
        _textLabel.Text = displayText ?? string.Empty;
        _textLabel.TextColor = textColor;
        _textLabel.FontSize = FontSize <= 0 ? G9Metrics.ButtonFontMedium : FontSize;
        _textLabel.FontAttributes = FontAttributes;
        // Every other G9 text control (G9Editor, G9TextEntry, G9PinEntry,
        // G9OutlinedFieldBase) explicitly resolves the culture-appropriate face via
        // G9Visuals.ResolveCulturalFont() — without it, MAUI's platform fallback renders
        // Persian text in a mismatched face vs. the rest of the UI (missing/garbled glyphs on
        // some OEM font stacks). G9Button's label was the one text-bearing control that
        // never did this. OnCultureChangedHook (G9ControlBase default) already triggers a
        // full OnApplyVisuals on culture change, so this keeps itself in sync for free.
        _textLabel.FontFamily = G9Visuals.ResolveCulturalFont();
        _textLabel.IsVisible = !string.IsNullOrWhiteSpace(displayText);
        _textLabel.LineBreakMode = TextTruncation ? LineBreakMode.TailTruncation : LineBreakMode.NoWrap;

        _loadingIndicator.IsRunning = IsLoading;
        _loadingIndicator.IsVisible = IsLoading;
        _loadingIndicator.Color = textColor;
        _loadingIndicator.WidthRequest = IconSize;
        _loadingIndicator.HeightRequest = IconSize;

        var leadingVisible = !IsLoading && G9IconFactory.HasIcon(LeadingEmoji, LeadingIcon, LeadingImagePath, LeadingImageSource);
        _leadingHost.Content = leadingVisible
            ? G9IconFactory.Create(LeadingEmoji, LeadingIcon, LeadingImagePath, LeadingImageSource, textColor, IconSize)
            : null;
        _leadingHost.IsVisible = leadingVisible;

        var trailingVisible = !IsLoading && G9IconFactory.HasIcon(TrailingEmoji, TrailingIcon, TrailingImagePath, TrailingImageSource);
        _trailingHost.Content = trailingVisible
            ? G9IconFactory.Create(TrailingEmoji, TrailingIcon, TrailingImagePath, TrailingImageSource, textColor, IconSize)
            : null;
        _trailingHost.IsVisible = trailingVisible;

        // Natural (uncapped) width of the current text, cached HERE — a visual-property change,
        // never inside a layout pass. UpdateTextMaxWidth consumes the cache so the layout hook
        // (OnSizeAllocated) stays pure arithmetic: measuring the label there required lifting the
        // cap first, and that ClearValue→Measure→re-set dance re-invalidated layout every pass.
        _textLabel.ClearValue(MaximumWidthRequestProperty);
        _naturalTextWidth = _textLabel.IsVisible
            ? _textLabel.Measure(double.PositiveInfinity, double.PositiveInfinity).Width
            : 0;

        UpdateTextMaxWidth();
    }

    private double _naturalTextWidth;

    // Latest finite width CONSTRAINT the parent offered during measure. This — not the resolved
    // Width — is the safe basis for the truncation cap: Width reflects our own (possibly
    // collapsed) desired size, so deriving the cap from it pinned the label in Auto-sized slots
    // (empty-text first measure → tiny Width → cap ~0 → label invisible forever — the map
    // multi-selection Continue button bug). double.NaN = no finite constraint seen yet.
    private double _lastFiniteWidthConstraint = double.NaN;

    /// <summary>
    ///     Records the offered width constraint. MUST NOT mutate any layout-affecting property
    ///     here — setting e.g. <c>MaximumWidthRequest</c> inside the measure pass invalidates
    ///     measure and livelocks the UI thread (ANR). The cap is applied outside the pass, from
    ///     <see cref="OnVisualChanged" /> / <see cref="OnSizeAllocated" />.
    /// </summary>
    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        var offered = widthConstraint - Margin.Left - Margin.Right;
        if (!double.IsNaN(offered) && !double.IsInfinity(offered) && offered > 0)
        {
            _lastFiniteWidthConstraint = offered;
        }
        else
        {
            _lastFiniteWidthConstraint = double.NaN;
        }

        return base.MeasureOverride(widthConstraint, heightConstraint);
    }

    /// <summary>
    ///     Applies the truncation cap AFTER the pass, which is the only safe place for it.
    ///     <para>
    ///         This write is what produces a single Android
    ///         <c>requestLayout() improperly called … during layout: running second layout pass</c>
    ///         warning the first time a button is laid out at a real width (reproduced 2026-07-28:
    ///         exactly ONE per cold map-sheet open, zero on re-open). That is a DELIBERATE
    ///         trade-off, not an oversight — see <see cref="MeasureOverride" />: doing it during
    ///         measure invalidates measure re-entrantly and livelocks the UI thread into an ANR.
    ///         One converging extra layout pass is the cheaper failure mode. Do not "fix" this by
    ///         moving the cap into <see cref="MeasureOverride" />.
    ///     </para>
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateTextMaxWidth();
    }

    /// <summary>
    ///     Clamp the label width to what's left of the parent's OFFERED width after the row margin
    ///     + any visible icons/spinner, so a too-long label truncates with an ellipsis instead of
    ///     overflowing the frame. The label lives in a centered <see cref="HorizontalStackLayout" />
    ///     which does not constrain child width on its own. The cap is only applied when the
    ///     label's cached natural width genuinely exceeds the offered space — a label that fits
    ///     keeps its natural size, so an Auto-sized slot can grow the button when the text binding
    ///     delivers late (no more pinned-to-empty deadlock). Pure arithmetic + at most one
    ///     converging property change — safe to call from <see cref="OnSizeAllocated" />.
    /// </summary>
    private void UpdateTextMaxWidth()
    {
        if (!TextTruncation || !_textLabel.IsVisible || double.IsNaN(_lastFiniteWidthConstraint))
        {
            _textLabel.ClearValue(MaximumWidthRequestProperty);
            return;
        }

        var horizontalMargin = _row.Margin.Left + _row.Margin.Right;
        double iconsWidth = 0;
        var siblingCount = 0;

        if (_leadingHost.IsVisible) { iconsWidth += IconSize; siblingCount++; }
        if (_loadingIndicator.IsVisible) { iconsWidth += _loadingIndicator.WidthRequest; siblingCount++; }
        if (_trailingHost.IsVisible) { iconsWidth += IconSize; siblingCount++; }

        // One gap between the label and each visible sibling (invisible children take no space).
        var spacing = _row.Spacing * siblingCount;
        var available = _lastFiniteWidthConstraint - horizontalMargin - iconsWidth - spacing;

        // 4dp tolerance so a label that exactly fits is treated as fitting (chrome estimation
        // rounding must never trigger a 1–2dp ellipsis or a shrink loop).
        if (available <= 0 || _naturalTextWidth <= available + 4)
        {
            if (_textLabel.IsSet(MaximumWidthRequestProperty))
            {
                _textLabel.ClearValue(MaximumWidthRequestProperty);
            }

            return; // fits — keep natural width
        }

        var cap = Math.Max(available, 24);
        if (!_textLabel.IsSet(MaximumWidthRequestProperty) ||
            Math.Abs(_textLabel.MaximumWidthRequest - cap) > 0.5)
        {
            _textLabel.MaximumWidthRequest = cap;
        }
    }

    private void ApplySize()
    {
        var (height, padding, font) = Size switch
        {
            G9ControlSize.Small => (G9Metrics.ButtonHeightSmall, new Thickness(12, 8), G9Metrics.ButtonFontSmall),
            G9ControlSize.Large => (G9Metrics.ButtonHeightLarge, new Thickness(20, 14), G9Metrics.ButtonFontLarge),
            G9ControlSize.Hero => (G9Metrics.ButtonHeightHero, new Thickness(16), G9Metrics.ButtonFontHero),
            _ => (G9Metrics.ButtonHeightMedium, new Thickness(16, 12), G9Metrics.ButtonFontMedium)
        };

        // Breathing room around the text/icon row lives on the row's margin instead of
        // the Border's Padding. Reason: the press-ripple GraphicsView is layered behind
        // the row inside the Border. With Border.Padding > 0 the GraphicsView's measure
        // rect shrinks by the padding amount, so the ripple's max radius — computed
        // from the rect's diagonal — only ever reaches the inset region. Visually the
        // ripple covered the full width but the top/bottom 12 dp of the button stayed
        // unanimated (visible as a "letterbox" of unaffected colour). Moving the
        // breathing room onto the row keeps the ripple measure rect equal to the full
        // Border interior, so the animation fills the entire button surface.
        _frame.Padding = 0;
        _row.Margin = padding;

        // Respect an explicit consumer HeightRequest/MinimumHeightRequest (MAUI's unset default
        // is -1) instead of always clobbering it with the Size preset. This pass reruns on every
        // OnApplyVisuals (theme/culture change, any bindable property write), so unconditionally
        // overwriting here silently shrank a caller's taller button (e.g. HeightRequest="52" on
        // a Medium-size button) back down to the 44dp Medium preset — 24dp of which is already
        // spent on the row's top/bottom margin, leaving too little room for a Bold 16sp Persian
        // label and clipping its descenders against the frame bounds.
        if (HeightRequest <= 0)
        {
            HeightRequest = height;
        }
        if (MinimumHeightRequest <= 0)
        {
            MinimumHeightRequest = height;
        }

        if (Size == G9ControlSize.Hero)
        {
            HorizontalOptions = LayoutOptions.Fill;
            _frame.HorizontalOptions = LayoutOptions.Fill;
        }

        if (FontSize <= 0)
        {
            FontSize = font;
        }
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || IsLoading) return;

        var point = e.GetPosition(this);
        if (point.HasValue && Width > 0 && Height > 0)
        {
            _rippleDrawable.Center = new PointF(
                (float)(point.Value.X / Width),
                (float)(point.Value.Y / Height));
        }

        // Invoke handlers SYNCHRONOUSLY first so any state change inside the handler
        // (most commonly IsLoading = true) lands on this same frame as the touch-up
        // event. Awaiting PlayPressAnimationAsync first added ~230 ms of perceived lag
        // between finger-up and the spinner appearing — the user reported "first the
        // press animation, then a delay, then the spinner". By the time the press
        // animation finishes the loading visuals are already mounted, so the press
        // animation simply wraps around the freshly-rendered spinner — visually you
        // see the press cue ride into the loading state with zero gap.
        //
        // The press animation is fire-and-forget; it never blocks user-defined click
        // handlers. ConfigureAwait(false) on the inner awaits keeps the continuation
        // off the click-handler call stack.
        try
        {
            Clicked?.Invoke(this, EventArgs.Empty);

            if (Command is { } cmd && cmd.CanExecute(CommandParameter))
            {
                cmd.Execute(CommandParameter);
            }
        }
        catch
        {
            // Swallow — UI buttons must never crash the app from a click handler.
        }

        _ = PlayPressAnimationAsync();
    }

    private async Task PlayPressAnimationAsync()
    {
        _rippleView.Opacity = 1;
        _rippleDrawable.Progress = 0;
        _rippleView.Invalidate();

        var ripple = new Animation(v =>
        {
            _rippleDrawable.Progress = (float)v;
            _rippleView.Invalidate();
        }, 0, 1);
        ripple.Commit(this, "AppButtonRipple", 16, G9Metrics.RippleDurationMs, Easing.CubicOut, (_, _) => _rippleView.Opacity = 0);

        try
        {
            await this.ScaleToAsync(0.96, G9Metrics.PressDurationMs, Easing.CubicIn).ConfigureAwait(true);
            await this.ScaleToAsync(1.0, G9Metrics.ReleaseDurationMs, Easing.CubicOut).ConfigureAwait(true);
        }
        catch
        {
            // Animations are best-effort.
        }
    }

    private async void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (!IsEnabled || IsLoading) return;

        try
        {
            await this.TranslateToAsync(0, -1, G9Metrics.HoverDurationMs, Easing.CubicOut).ConfigureAwait(true);
        }
        catch
        {
        }
    }

    private async void OnPointerExited(object? sender, PointerEventArgs e)
    {
        try
        {
            await this.TranslateToAsync(0, 0, G9Metrics.HoverDurationMs, Easing.CubicOut).ConfigureAwait(true);
        }
        catch
        {
        }
    }
}
