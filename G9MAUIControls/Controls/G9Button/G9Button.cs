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

    // Stable paint state (G9Controls.md §12). The stroke brush lives as long as the button and
    // only its Color is mutated; the background brush and the two icon views are rebuilt only
    // when what they show actually changes, not on every apply pass.
    private readonly SolidColorBrush _strokeBrush = new(Colors.Transparent);
    private Color? _backgroundBrushColor;
    private bool _backgroundBrushIsGradient;
    private G9IconSlotSignature _leadingIconSignature;
    private G9IconSlotSignature _trailingIconSignature;

    // What ApplySize last wrote on the consumer-facing layout properties. A preset may only ever
    // overwrite its OWN earlier write — never a value the consumer set.
    private double _presetHeightRequest = double.NaN;
    private double _presetMinimumHeightRequest = double.NaN;
    private bool _heroFillApplied;
    private LayoutOptions _horizontalOptionsBeforeHero;

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

    /// <summary>
    ///     Optional label font size OVERRIDE. Left unset, the label takes the <see cref="Size" />
    ///     preset's font (12 / 14 / 15 / 16). The property default mirrors the Medium preset, so
    ///     reading it on an untouched button still returns 14.
    /// </summary>
    [AutoBindable(DefaultValue = "G9Metrics.ButtonFontMedium", OnChanged = nameof(OnVisualChanged))]
    private double _fontSize;
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
            // The radius is a constant, so the shape is built exactly once, here.
            StrokeShape = G9Colors.Round(G9Metrics.RadiusMd),
            Stroke = _strokeBrush,
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
        // FontSize is deliberately NOT assigned here. Assigning it made the property "set" on
        // every button, which is exactly what defeated the Size presets: ApplySize could not
        // tell a consumer's FontSize from this constructor's, so the 12 / 15 / 16 preset fonts
        // never applied. See ResolveFontSize.
        FontAttributes = FontAttributes.Bold;
        TextTruncation = true;
    }

    public event EventHandler? Clicked;

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <summary>
    ///     Subclass hook: <c>true</c> while the button must refuse taps and paint disabled for a
    ///     reason of its own (busy, command cannot execute). Exists so <see cref="G9SafeButton" />
    ///     never has to write <see cref="VisualElement.IsEnabled" />, which belongs to the consumer
    ///     — writing it replaced their <c>IsEnabled="{Binding …}"</c>.
    /// </summary>
    private protected virtual bool IsInteractionBlocked => false;

    /// <summary>
    ///     Subclass hook: whether a tap runs <see cref="Command" /> directly.
    ///     <see cref="G9SafeButton" /> turns it off because it runs the same command through the
    ///     safe layer from <see cref="Clicked" />; leaving both on would execute it twice.
    /// </summary>
    private protected virtual bool ExecutesCommandOnTap => true;

    /// <summary>The consumer's <see cref="VisualElement.IsEnabled" /> AND the button's own veto.</summary>
    private bool IsEffectivelyEnabled => IsEnabled && !IsInteractionBlocked;

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
        var enabled = IsEffectivelyEnabled && !IsLoading;
        ApplyFrameBrushes(colors, enabled);
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
        var effectivelyEnabled = IsEffectivelyEnabled;
        var enabled = effectivelyEnabled && !IsLoading;
        var hasCustomBg = BaseBackgroundColor is not null;
        var isOutlineLike = !hasCustomBg && Variant is G9ButtonVariant.Outline or G9ButtonVariant.Text;

        // Custom-background buttons get a hairline border from the derived stroke (matches
        // the legacy G9SafeButton look); variant buttons keep the outline/tonal stroke rule.
        var strokeThickness = hasCustomBg
            ? (colors.Stroke.Alpha > 0f ? 1 : 0)
            : (isOutlineLike || Variant is G9ButtonVariant.Tonal ? 1.5 : 0);
        if (_frame.StrokeThickness != strokeThickness) _frame.StrokeThickness = strokeThickness;
        ApplyFrameBrushes(colors, enabled);

        // G9Button paints NO drop shadow, ever — the app is shadow-free by policy. Elevation is
        // expressed with the frame's Background / Stroke only. See G9Controls.md → "No shadows".
        Opacity = !effectivelyEnabled ? 0.38 : IsLoading ? 0.70 : 1;

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
        _textLabel.FontSize = ResolveFontSize();
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

        // Icons are built once per distinct icon and merely HIDDEN while loading — never torn
        // down and rebuilt around the spinner, which re-decoded bitmaps and flashed glyphs on
        // every press (G9Controls.md §12a).
        var hasLeading = G9IconSlot.Apply(
            _leadingHost, ref _leadingIconSignature,
            LeadingEmoji, LeadingIcon, LeadingImagePath, LeadingImageSource, textColor, IconSize);
        _leadingHost.IsVisible = hasLeading && !IsLoading;

        var hasTrailing = G9IconSlot.Apply(
            _trailingHost, ref _trailingIconSignature,
            TrailingEmoji, TrailingIcon, TrailingImagePath, TrailingImageSource, textColor, IconSize);
        _trailingHost.IsVisible = hasTrailing && !IsLoading;

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
        var (height, padding) = Size switch
        {
            G9ControlSize.Small => (G9Metrics.ButtonHeightSmall, new Thickness(12, 8)),
            G9ControlSize.Large => (G9Metrics.ButtonHeightLarge, new Thickness(20, 14)),
            G9ControlSize.Hero => (G9Metrics.ButtonHeightHero, new Thickness(16)),
            _ => (G9Metrics.ButtonHeightMedium, new Thickness(16, 12))
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
        if (_row.Margin != padding) _row.Margin = padding;

        // Respect an explicit consumer HeightRequest/MinimumHeightRequest (MAUI's unset default
        // is -1) instead of always clobbering it with the Size preset. This pass reruns on every
        // OnApplyVisuals (theme/culture change, any bindable property write), so unconditionally
        // overwriting here silently shrank a caller's taller button (e.g. HeightRequest="52" on
        // a Medium-size button) back down to the 44dp Medium preset — 24dp of which is already
        // spent on the row's top/bottom margin, leaving too little room for a Bold 16sp Persian
        // label and clipping its descenders against the frame bounds.
        //
        // "Respect" means: write only while the property is unset OR still holds what THIS method
        // wrote last time. The earlier `<= 0` test alone meant the first preset stuck forever —
        // a later Size change never resized the button, because our own write looked explicit.
        if (HeightRequest <= 0 || HeightRequest == _presetHeightRequest)
        {
            if (HeightRequest != height) HeightRequest = height;
            _presetHeightRequest = height;
        }
        if (MinimumHeightRequest <= 0 || MinimumHeightRequest == _presetMinimumHeightRequest)
        {
            if (MinimumHeightRequest != height) MinimumHeightRequest = height;
            _presetMinimumHeightRequest = height;
        }

        if (Size == G9ControlSize.Hero)
        {
            if (!_heroFillApplied)
            {
                _heroFillApplied = true;
                _horizontalOptionsBeforeHero = HorizontalOptions;
            }
            HorizontalOptions = LayoutOptions.Fill;
            _frame.HorizontalOptions = LayoutOptions.Fill;
        }
        else if (_heroFillApplied)
        {
            // Leaving Hero: undo our Fill — but only if it is still ours. A consumer who set
            // their own alignment meanwhile keeps it. (The frame's Fill is a View's default, so
            // there is nothing to restore there.)
            _heroFillApplied = false;
            if (HorizontalOptions.Alignment == LayoutAlignment.Fill)
            {
                HorizontalOptions = _horizontalOptionsBeforeHero;
            }
        }
    }

    /// <summary>
    ///     The label's font size: an explicit <see cref="FontSize" /> wins, otherwise the
    ///     <see cref="Size" /> preset. The preset is applied to the LABEL only and is never written
    ///     back onto <see cref="FontSize" /> — that write-back is what used to make the property
    ///     look consumer-set and froze the font at the first preset.
    /// </summary>
    private double ResolveFontSize()
    {
        if (IsSet(FontSizeProperty) && FontSize > 0)
        {
            return FontSize;
        }

        return Size switch
        {
            G9ControlSize.Small => G9Metrics.ButtonFontSmall,
            G9ControlSize.Large => G9Metrics.ButtonFontLarge,
            G9ControlSize.Hero => G9Metrics.ButtonFontHero,
            _ => G9Metrics.ButtonFontMedium
        };
    }

    /// <summary>
    ///     Pushes stroke and background onto the frame without churning brushes: the stroke brush
    ///     is one instance whose colour is mutated, and the background brush is rebuilt only when
    ///     its colour or its solid / gradient kind actually changes (disabled and loading states
    ///     flatten the gradient, so the kind is part of the key).
    /// </summary>
    private void ApplyFrameBrushes(G9Visuals.ButtonVisualResult colors, bool enabled)
    {
        if (!Equals(_strokeBrush.Color, colors.Stroke)) _strokeBrush.Color = colors.Stroke;

        var gradient = colors.UsesGradient && enabled;
        if (_frame.Background is not null
            && _backgroundBrushIsGradient == gradient
            && Equals(_backgroundBrushColor, colors.Background))
        {
            return;
        }

        _backgroundBrushColor = colors.Background;
        _backgroundBrushIsGradient = gradient;
        _frame.Background = G9Colors.BuildSolidOrGradient(colors.Background, gradient);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEffectivelyEnabled || IsLoading) return;

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

            if (ExecutesCommandOnTap && Command is { } cmd && cmd.CanExecute(CommandParameter))
            {
                cmd.Execute(CommandParameter);
            }
        }
        catch (Exception ex)
        {
            // A UI button must never crash the app from a click handler — but it must not hide
            // the failure either: a swallowed exception is a button that "does nothing" with no
            // trace of why.
            G9Press.ReportFailure(this, ex);
        }

        _ = PlayPressAnimationAsync();
    }

    private async Task PlayPressAnimationAsync()
    {
        // Committing under the same name aborts the previous ripple, whose `finished` then runs
        // with cancelled == true — AFTER the Opacity = 1 below. It must not touch Opacity in
        // that case: the unconditional `Opacity = 0` hid the ripple of every second tap made
        // within the ripple's duration.
        _rippleView.Opacity = 1;
        _rippleDrawable.Progress = 0;
        _rippleView.Invalidate();

        var ripple = new Animation(v =>
        {
            _rippleDrawable.Progress = (float)v;
            _rippleView.Invalidate();
        }, 0, 1);
        ripple.Commit(this, "AppButtonRipple", 16, G9Metrics.RippleDurationMs, Easing.CubicOut,
            (_, cancelled) =>
            {
                if (!cancelled) _rippleView.Opacity = 0;
            });

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
        if (!IsEffectivelyEnabled || IsLoading) return;

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
