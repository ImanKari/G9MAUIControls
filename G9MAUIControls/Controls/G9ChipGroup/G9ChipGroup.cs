using G9MAUIControls.Helpers;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Layouts;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Wrapping chip group with single- or multi-selection.
///     <para>
///         <b>Animation architecture (the part that took several iterations to get right):</b>
///         The previous implementations recreated the icon View at the end of the selection
///         animation and swapped the chip's <see cref="Brush" /> type from a transient
///         <see cref="SolidColorBrush" /> back to a <see cref="LinearGradientBrush" /> on the
///         final frame. Both caused visible flashes — recreating an icon makes the new
///         platform view render one frame with its default color before the mapper applies
///         the explicit color, and swapping brush types causes a visible "pop" at t=1.
///     </para>
///     <para>
///         The new approach is destruction-free:
///         <list type="bullet">
///             <item>Each chip's icon View is built ONCE in <see cref="BuildChip" /> and never
///             replaced.</item>
///             <item>Each chip owns stable <see cref="SolidColorBrush" /> instances for its
///             background and stroke. We mutate <see cref="SolidColorBrush.Color" /> per frame
///             instead of allocating new brushes.</item>
///             <item>There is NO selection shadow — the app is shadow-free by policy (see
///             <c>G9Controls.md</c>). Selection is carried by the background gradient,
///             the stroke tint, the text colour and the checkmark.</item>
///             <item>Color updates for the icon are property mutations on the existing View:
///             <see cref="Label.TextColor" /> for emoji icons, <see cref="G9IconView.Color" />
///             for Material icons. Image-source icons are not tinted.</item>
///         </list>
///         The result: a single coherent animation where every visible property
///         (background, stroke, text, icon, shadow) interpolates in lockstep with no platform
///         re-init flicker and no brush-type pop.
///     </para>
///     <para>
///         <b>Other rules:</b> Only the chip whose state actually changed plays the
///         transition; every other chip stays untouched so toggling one chip never blinks
///         the rest of the group.
///     </para>
///     // TODO (palette step): selected chip background / shadow / outline tokens move to G9Palette.
/// </summary>
public partial class G9ChipGroup : G9ControlBase
{
    /// <summary>Host for <see cref="G9ChipGroupLayoutMode.Wrap" /> — chips flow onto more lines.</summary>
    private readonly FlexLayout _wrapHost;

    /// <summary>
    ///     Host for <see cref="G9ChipGroupLayoutMode.SingleLineScroll" />. Deliberately a
    ///     <see cref="HorizontalStackLayout" /> and NOT the wrapping <see cref="FlexLayout" />: a wrapping
    ///     FlexLayout inside a horizontal <see cref="ScrollView" /> measures against an infinite width and
    ///     degenerates — every chip ends up on its own row (the "chips stuck vertically" bug that made
    ///     <c>GroupItems/ScrollableChipGroup</c> drop its ScrollView). A stack has no wrap to degenerate.
    /// </summary>
    private readonly HorizontalStackLayout _lineHost;
    private readonly ScrollView _lineScroll;

    private readonly Dictionary<object, ChipBinding> _bindings = [];
    private ObservableCollection<G9SelectionItem>? _attachedItems;
    private ObservableCollection<G9SelectionItem>? _attachedSelected;

    [AutoBindable(OnChanged = nameof(OnItemsSourceChanged))]
    private ObservableCollection<G9SelectionItem>? _itemsSource;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnSelectedItemsChanged))]
    private ObservableCollection<G9SelectionItem>? _selectedItems;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnSelectedItemChanged))]
    private G9SelectionItem? _selectedItem;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9ChipGroupSelectionMode _selectionMode;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _allowNullSelection;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _itemSpacing;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _chipHeight;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _iconSize;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _selectedBackground;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _selectedTextColor;

    /// <summary>Wrap onto more lines (default) vs stay on one horizontally-scrolling line.</summary>
    [AutoBindable(OnChanged = nameof(OnLayoutModeChanged))]
    private G9ChipGroupLayoutMode _layoutMode;

    /// <summary>
    ///     Corner radius of every chip in the group. Defaults to the app's shared
    ///     <see cref="G9LayoutMetrics.ControlCornerRadius" /> (9) — the SAME token the task cards and
    ///     the other rounded surfaces use, so a chip reads as part of the same family instead of as a
    ///     stray pill. Set it to <c>G9Metrics.RadiusPill</c> (999) for the old fully-rounded look.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnChipCornerRadiusChanged))]
    private double _chipCornerRadius;

    /// <summary>
    ///     Whether a selected chip grows the trailing Material 3 checkmark. Default <c>true</c>. Turn it OFF
    ///     for chips that already carry their own meaningful icon (the Tasks state filters: hourglass /
    ///     in-progress / done) — there the check is a second, redundant glyph and its width animation
    ///     reflows the chip on every tap.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnShowSelectionCheckmarkChanged))]
    private bool _showSelectionCheckmark;

    public G9ChipGroup()
    {
        _wrapHost = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            JustifyContent = FlexJustify.Start,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start
        };

        // Spacing = 0: the chips carry their own trailing/bottom margin (see BuildChip), so the strip has
        // the same geometry in both layout modes and the bottom margin doubles as clearance for the
        // selected chip's shadow inside the clipping ScrollView.
        _lineHost = new HorizontalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Start
        };

        _lineScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalOptions = LayoutOptions.Start,
            Content = _lineHost
        };

        Content = _wrapHost;

        SelectedItems = [];
        SelectionMode = G9ChipGroupSelectionMode.MultiSelection;
        ItemSpacing = G9Metrics.ChipSpacing;
        ChipHeight = G9Metrics.ChipHeight;
        IconSize = G9Metrics.ChipIconSize;

        // [AutoBindable] ignores field initializers — the generated BindableProperty defaults are
        // default(T) (false / 0). Both of these need a real default, so assign them here.
        // The checkmark is opt-OUT; the radius tracks the app-wide control radius (cards, borders) so
        // retuning that one token restyles the chips with everything else.
        ShowSelectionCheckmark = true;
        ChipCornerRadius = G9LayoutMetrics.ControlCornerRadius;
    }

    public event EventHandler<IReadOnlyList<G9SelectionItem>>? SelectionChanged;

    /// <summary>
    ///     Cached chip widget set + stable brush / shadow / checkmark instances + current
    ///     animated value tracking. The Border, inner Row, TextLabel, IconView, and
    ///     CheckmarkView are built once in <see cref="BuildChip" /> and never replaced —
    ///     property changes happen by mutating fields on these existing instances.
    /// </summary>
    private sealed class ChipBinding
    {
        public required G9SelectionItem Item { get; init; }
        public required Border Chip { get; init; }
        public required Label TextLabel { get; init; }
        public View? IconView { get; init; }
        public required LinearGradientBrush BackgroundBrush { get; init; }
        public required GradientStop BackgroundStopTop { get; init; }
        public required GradientStop BackgroundStopMid { get; init; }
        public required GradientStop BackgroundStopBottom { get; init; }
        public required SolidColorBrush StrokeBrush { get; init; }

        /// <summary>
        ///     The Material 3 selected-state checkmark. A <see cref="G9IconView" /> wrapped
        ///     in a host whose <see cref="VisualElement.WidthRequest" />,
        ///     <see cref="VisualElement.Opacity" />, and <see cref="VisualElement.Scale" />
        ///     animate together when the chip's selection state toggles. The G9IconView's
        ///     <see cref="G9IconView.Color" /> follows the active text color so the
        ///     check matches the chip's content tint.
        ///     <para>
        ///         NULL when <see cref="ShowSelectionCheckmark" /> is false — the widgets are then never
        ///         built, so the chip keeps a fixed width and selection is carried by color + shadow alone.
        ///     </para>
        /// </summary>
        public ContentView? CheckmarkHost { get; init; }
        public G9IconView? CheckmarkIcon { get; init; }

        // Live tracking — used as the "from" value of a fresh animation when one starts
        // mid-flight (e.g., user double-taps the chip). Without this, mid-animation
        // cancellation would snap to the resting state before re-interpolating.
        public Color CurrentBg { get; set; } = Colors.Transparent;
        public Color CurrentStroke { get; set; } = Colors.Transparent;
        public Color CurrentText { get; set; } = Colors.Transparent;
        public double CurrentCheckProgress { get; set; }

        public bool IsSelected { get; set; }
    }

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <summary>Swaps the host and re-parents the chips (a chip can only live in one layout at a time).</summary>
    private void OnLayoutModeChanged()
    {
        Content = LayoutMode == G9ChipGroupLayoutMode.SingleLineScroll ? _lineScroll : _wrapHost;
        RebuildAll();
    }

    /// <summary>The checkmark widgets are built (or not) in <see cref="BuildChip" />, so this needs a rebuild.</summary>
    private void OnShowSelectionCheckmarkChanged() => RebuildAll();

    /// <summary>Reshape the live chips in place — the radius is pure geometry, nothing needs rebuilding.</summary>
    private void OnChipCornerRadiusChanged()
    {
        foreach (var (_, binding) in _bindings)
        {
            binding.Chip.StrokeShape = G9Colors.Round(ChipCornerRadius);
        }
    }

    private Layout ActiveHost =>
        LayoutMode == G9ChipGroupLayoutMode.SingleLineScroll ? _lineHost : _wrapHost;

    private void OnItemsSourceChanged()
    {
        if (_attachedItems is not null) _attachedItems.CollectionChanged -= OnSourceChanged;
        _attachedItems = ItemsSource;
        if (_attachedItems is not null) _attachedItems.CollectionChanged += OnSourceChanged;
        RebuildAll();
    }

    private void OnSelectedItemsChanged()
    {
        if (_attachedSelected is not null) _attachedSelected.CollectionChanged -= OnSelectionChangedExt;
        _attachedSelected = SelectedItems;
        if (_attachedSelected is not null) _attachedSelected.CollectionChanged += OnSelectionChangedExt;
        ApplySelectionState(animate: true);
    }

    private void OnSelectedItemChanged() => ApplySelectionState(animate: true);

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildAll();

    private void OnSelectionChangedExt(object? sender, NotifyCollectionChangedEventArgs e) => ApplySelectionState(animate: true);

    protected override void OnApplyVisuals()
    {
        if (ActiveHost.Children.Count == 0)
        {
            RebuildAll();
            return;
        }

        // Refresh selection state without animation (theme/palette change).
        // Existing chip widgets stay; we just mutate their current colors to the new
        // resting palette.
        foreach (var (_, binding) in _bindings)
        {
            ApplyChipState(binding, binding.IsSelected, animate: false);
        }
    }

    private void RebuildAll()
    {
        // Clear BOTH hosts: a layout-mode switch rebuilds into the new host, and a chip left parented to
        // the old one would be a stale duplicate.
        _wrapHost.Children.Clear();
        _lineHost.Children.Clear();
        _bindings.Clear();

        var items = ItemsSource;
        if (items is null) return;

        var host = ActiveHost;
        foreach (var item in items)
        {
            var binding = BuildChip(item);
            _bindings[item] = binding;
            host.Children.Add(binding.Chip);
            ApplyChipState(binding, IsSelected(item), animate: false);
        }
    }

    private ChipBinding BuildChip(G9SelectionItem item)
    {
        var palette = G9Palette.Current;

        // Build the icon View ONCE. We never destroy or re-create it during selection
        // changes — color updates happen by mutating Label.TextColor / G9IconView.Color
        // on this existing instance. Recreating the View previously caused a 1-frame
        // platform-default-color flash (visible as a brief white flash on toggle).
        var hasIcon = G9IconFactory.HasIcon(item.Emoji, item.Icon, item.IconPath, item.IconSource);
        View? iconView = hasIcon
            ? G9IconFactory.Create(
                item.Emoji, item.Icon, item.IconPath, item.IconSource,
                palette.TextSecondary, IconSize, 4)
            : null;

        var label = new Label
        {
            Text = item.Text,
            FontSize = G9Metrics.ChipFontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = palette.TextSecondary,
            LineBreakMode = LineBreakMode.NoWrap,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        };

        var row = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        // Material 3 selected-state checkmark, slotted at the TRAILING edge of the row
        // (after any custom icon, after the label). Hosted in a fixed-anchor
        // ContentView so we can animate WidthRequest from 0 → IconSize independently
        // of the row's natural layout — that's what produces the M3 "label slides
        // left to make room for the check" effect.
        //
        // The check sits AFTER the user-provided icon so it never collides visually
        // with chips that carry their own leading icon (e.g. droplet, weather sun).
        // An earlier version placed the check at the leading edge — that put two
        // icons stacked at the start of selected chips and confused users about
        // which was the chip's identity icon vs the selection state.
        //
        // AnchorX = 1 so the Scale animation grows from the right edge (paired with
        // the width animation) instead of expanding from the geometric center of the
        // host (which would push half the check outside the host while it grows).
        // With AnchorX = 1 the check feels like it "extrudes" out of the chip's
        // trailing edge — exactly the M3 spec motion, just mirrored to the new side.
        //
        // Skipped entirely when ShowSelectionCheckmark is false: the chip then carries only its own icon
        // and label, keeps a constant width across selection changes, and shows selection through the
        // background / stroke / shadow crossfade.
        G9IconView? checkmarkIcon = null;
        ContentView? checkmarkHost = null;
        if (ShowSelectionCheckmark)
        {
            checkmarkIcon = new G9IconView {
                Icon = G9Glyphs.Check,
                Size = G9Metrics.ChipIconSize,
                Color = palette.OnPrimary,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            checkmarkHost = new ContentView
            {
                Content = checkmarkIcon,
                WidthRequest = 0,
                Opacity = 0,
                Scale = 0.5,
                AnchorX = 1,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
        }

        if (iconView is not null) row.Children.Add(iconView);
        row.Children.Add(label);
        if (checkmarkHost is not null) row.Children.Add(checkmarkHost);

        // Stable brush instances. We mutate .Color (or GradientStop.Color) per animation
        // frame instead of allocating new brushes — this lets the platform handler observe
        // a "color changed" notification (cheap) rather than "brush replaced" (expensive,
        // can trigger full re-paint). Critically the BRUSH TYPE stays the same throughout
        // the animation so there's no visible "pop" at t=1 (which the previous version
        // had when swapping SolidColorBrush → LinearGradientBrush on the final frame).
        var bgStopTop = new GradientStop { Offset = 0f, Color = palette.Surface };
        var bgStopMid = new GradientStop { Offset = 0.5f, Color = palette.Surface };
        var bgStopBottom = new GradientStop { Offset = 1f, Color = palette.Surface };
        var bgBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = [bgStopTop, bgStopMid, bgStopBottom]
        };
        var strokeBrush = new SolidColorBrush(palette.OutlineVariant);

        // No Shadow — the app is shadow-free by policy (see G9Controls.md). Selection is
        // carried by the background gradient, the stroke tint, the text colour and the checkmark,
        // all of which already animate in lockstep.
        var chip = new Border
        {
            HeightRequest = ChipHeight,
            MinimumHeightRequest = ChipHeight,
            Margin = new Thickness(0, 0, ItemSpacing, ItemSpacing),
            Padding = new Thickness(G9Metrics.ChipHorizontalPadding, 0),
            StrokeThickness = 1.5,
            Stroke = strokeBrush,
            StrokeShape = G9Colors.Round(ChipCornerRadius),
            Background = bgBrush,
            Content = row,
            BindingContext = item,
            VerticalOptions = LayoutOptions.Start
        };

        var binding = new ChipBinding
        {
            Item = item,
            Chip = chip,
            TextLabel = label,
            IconView = iconView,
            BackgroundBrush = bgBrush,
            BackgroundStopTop = bgStopTop,
            BackgroundStopMid = bgStopMid,
            BackgroundStopBottom = bgStopBottom,
            StrokeBrush = strokeBrush,
            CheckmarkHost = checkmarkHost,
            CheckmarkIcon = checkmarkIcon,
            IsSelected = false
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            // Toggle FIRST so ApplyChipState fires its color + checkmark animation on
            // the same frame as the touchup event. Zero perceived delay between
            // mouse-up / finger-lift and the selection animation kicking in.
            //
            // The press pulse runs in parallel as a fire-and-forget tactile cue. Its
            // animation handle is owned by ScaleTo (not the chip's "AppChipState"
            // handle), so it never interferes with the selection animation's
            // background / border / text / check transforms.
            //
            // The previous version awaited each ScaleToAsync sequentially, then called
            // Toggle. That made the user wait a combined ~220 ms (80 dip + 140 spring)
            // before the M3 checkmark started to appear — which is exactly the lag
            // the user was reporting.
            Toggle(item);
            _ = AnimateChipPressAsync(chip);
        };
        chip.GestureRecognizers.Add(tap);

        return binding;
    }

    private void ApplySelectionState(bool animate)
    {
        var items = ItemsSource;
        if (items is null) return;

        foreach (var item in items)
        {
            if (!_bindings.TryGetValue(item, out var binding)) continue;

            var nowSelected = IsSelected(item);
            if (binding.IsSelected == nowSelected)
            {
                continue; // Untouched — don't repaint, don't pulse.
            }

            ApplyChipState(binding, nowSelected, animate);
        }
    }

    private void ApplyChipState(ChipBinding binding, bool selected, bool animate)
    {
        var palette = G9Palette.Current;
        // Per-item selected colors (e.g. each task-state chip in its own foregroundColorCode) take
        // precedence over the group-wide SelectedBackground/SelectedTextColor; both fall back to the
        // theme. The UNSELECTED look is always the default chip style.
        var selectedBg = binding.Item.SelectedColor ?? SelectedBackground ?? palette.Primary;
        var selectedFg = binding.Item.SelectedTextColor ?? SelectedTextColor ?? palette.OnPrimary;
        var unselectedBg = palette.Surface;
        var unselectedStroke = palette.OutlineVariant;
        var unselectedFg = palette.TextSecondary;

        var fromBg = binding.CurrentBg;
        var fromStroke = binding.CurrentStroke;
        var fromText = binding.CurrentText;
        var fromCheck = binding.CurrentCheckProgress;

        var targetBg = selected ? selectedBg : unselectedBg;
        var targetStroke = selected ? selectedBg : unselectedStroke;
        var targetText = selected ? selectedFg : unselectedFg;
        var targetCheck = selected ? 1.0 : 0.0;

        binding.IsSelected = selected;

        if (!animate)
        {
            CommitState(binding, targetBg, targetStroke, targetText, targetCheck);
            return;
        }

        // Cancel any in-flight animation on this same chip so rapid toggles don't stack.
        // The CurrentBg/Stroke/Text/CheckProgress values were already updated by the last
        // frame of the cancelled animation, so the new animation's "from" is the exact
        // visible state — no jump.
        binding.Chip.AbortAnimation(AnimHandle);

        // Material 3 timing recipe:
        //   • The CHECKMARK animation is shorter (120ms) so the icon arrives just
        //     before the colors finish settling. This signals "selected" first, then
        //     reinforces with the color change — that ordering is what makes M3 chips
        //     feel responsive without feeling abrupt.
        //   • The COLOR animation runs the full 140ms with CubicInOut so it has a
        //     gentle ease-out tail.
        // We fold both into a single Animation that drives a 0..1 master timer; the
        // checkmark progress is sub-mapped to the first 120/140 of that timer (clamped
        // to 1 once the master reaches that fraction).
        const double checkmarkDurationFraction =
            (double)G9Metrics.ChipCheckmarkAnimationMs / G9Metrics.ChipSelectionAnimationMs;

        new Animation(t =>
        {
            // Color crossfade across the full t range.
            var bg = G9ColorHelper.Mix(fromBg, targetBg, t);
            var stroke = G9ColorHelper.Mix(fromStroke, targetStroke, t);
            var text = G9ColorHelper.Mix(fromText, targetText, t);

            // Checkmark progress runs faster (clamps to its target before the color
            // finishes settling). When entering the selected state the check appears
            // first; when leaving, it disappears first — same M3 motion either way.
            var checkRaw = Math.Min(1.0, t / checkmarkDurationFraction);
            var checkProgress = fromCheck + ((targetCheck - fromCheck) * checkRaw);

            CommitState(binding, bg, stroke, text, checkProgress);
        }, 0, 1, Easing.CubicInOut)
        .Commit(binding.Chip, AnimHandle, 16, G9Metrics.ChipSelectionAnimationMs, finished: (finalT, cancelled) =>
        {
            // Snap to exact target on the last frame so floating-point errors don't
            // leave a 0.001-alpha residue. If the animation was cancelled mid-flight
            // (a new ApplyChipState started), DON'T snap — the new animation already
            // owns the next frame and will paint over us.
            if (cancelled) return;
            CommitState(binding, targetBg, targetStroke, targetText, targetCheck);
        });
    }

    private const string AnimHandle = "AppChipState";

    /// <summary>
    ///     Tactile press cue: a brief scale dip (0.94 over 80 ms, ease-in) followed by
    ///     a spring back to 1.0 (140 ms). Fired-and-forgotten in parallel with the
    ///     selection animation so the user never waits for it. Catches any throw —
    ///     rapid taps can race the underlying ScaleTo and we don't want to surface
    ///     that as an unhandled exception.
    /// </summary>
    private static async Task AnimateChipPressAsync(Border chip)
    {
        try
        {
            await chip.ScaleToAsync(0.94, 80, Easing.CubicIn).ConfigureAwait(true);
            await chip.ScaleToAsync(1.0, 140, Easing.SpringOut).ConfigureAwait(true);
        }
        catch
        {
            // Best-effort feedback — visual outcome is still correct on race.
        }
    }

    /// <summary>
    ///     Pushes the given colors and check progress onto the chip's stable brush /
    ///     widget instances and updates the live tracking fields. We mutate
    ///     <see cref="GradientStop.Color" /> on the persistent gradient and
    ///     <see cref="SolidColorBrush.Color" /> on the stroke / shadow instances rather
    ///     than replacing the brushes — the platform handler observes a color-changed
    ///     notification, much cheaper than brush-replaced and crucially does NOT trigger
    ///     the brief default-color render frame that brush replacement causes.
    ///     <para>
    ///         The gradient is centered on <paramref name="bg" />: the top stop is 8%
    ///         lighter, the bottom stop is 8% darker — same recipe used by G9Button
    ///         (<see cref="G9Colors.BuildSolidOrGradient" />). On the unselected
    ///         resting state all three stops collapse to the surface color so the
    ///         gradient is invisible (a flat fill); the selected state spreads them apart
    ///         to produce the lit-edge depth that makes the chip stand out.
    ///     </para>
    ///     <para>
    ///         <b>Material 3 selection checkmark:</b> the trailing check icon's host
    ///         <see cref="VisualElement.WidthRequest" />, <see cref="VisualElement.Opacity" />
    ///         and <see cref="VisualElement.Scale" /> are all driven by
    ///         <paramref name="checkProgress" /> (0 = hidden, 1 = fully shown). Width
    ///         interpolates 0 → IconSize so the layout reflows smoothly as the check
    ///         appears/disappears (this is the M3 "label slides left to make room"
    ///         effect). Scale grows from 0.5 → 1 anchored at the trailing edge so the
    ///         check looks like it extrudes from the chip's end. The icon's color
    ///         tracks the resolved text color so it matches the rest of the chip's
    ///         content tint. Trailing placement keeps the chip's own leading icon
    ///         (water drop, weather sun, etc.) visually distinct from the selection
    ///         state — placing the check at the leading edge previously caused
    ///         "two icons stacked at the start" confusion on chips with their own
    ///         icon.
    ///     </para>
    /// </summary>
    private static void CommitState(ChipBinding binding, Color bg, Color stroke, Color text, double checkProgress)
    {
        binding.BackgroundStopTop.Color = G9ColorHelper.Lighten(bg, 0.08);
        binding.BackgroundStopMid.Color = bg;
        binding.BackgroundStopBottom.Color = G9ColorHelper.Darken(bg, 0.08);

        binding.StrokeBrush.Color = stroke;
        binding.TextLabel.TextColor = text;

        SetIconColor(binding.IconView, text);

        // Checkmark drive — three properties moving in lockstep produce the M3 motion.
        var clampedCheck = Math.Clamp(checkProgress, 0, 1);
        if (binding.CheckmarkHost is { } checkmarkHost && binding.CheckmarkIcon is { } checkmarkIcon)
        {
            checkmarkHost.WidthRequest = clampedCheck * G9Metrics.ChipIconSize;
            checkmarkHost.Opacity = clampedCheck;
            checkmarkHost.Scale = 0.5 + (0.5 * clampedCheck);
            checkmarkIcon.Color = text;
        }

        binding.CurrentBg = bg;
        binding.CurrentStroke = stroke;
        binding.CurrentText = text;
        binding.CurrentCheckProgress = clampedCheck;
    }

    /// <summary>
    ///     Updates the icon's tint color in-place. Each icon kind exposes a different
    ///     color property:
    ///     <list type="bullet">
    ///         <item><see cref="Label" /> (emoji): <see cref="Label.TextColor" /> tints
    ///         the glyph for monochrome emoji like ●, ✓, !</item>
    ///         <item><see cref="G9IconView" /> (Material): <see cref="G9IconView.Color" />
    ///         tints the vector icon</item>
    ///         <item><see cref="Border" /> with <see cref="Image" /> (raster): not
    ///         tinted — these are typically photos / illustrations meant to be shown
    ///         as-is</item>
    ///     </list>
    /// </summary>
    private static void SetIconColor(View? iconView, Color color)
    {
        switch (iconView)
        {
            case Label emojiLbl:
                emojiLbl.TextColor = color;
                break;
            case G9IconView mauiIcon:
                mauiIcon.Color = color;
                break;
            // Border-with-Image case: leave alone. Raster image icons (e.g. user
            // avatars, crop photos) aren't color-tinted with the chip state.
        }
    }

    private bool IsSelected(G9SelectionItem item)
    {
        if (SelectionMode == G9ChipGroupSelectionMode.SingleSelection)
        {
            return SelectedItem?.SelectionIdentity?.Equals(item.SelectionIdentity) == true;
        }

        return SelectedItems?.Any(s => s.SelectionIdentity?.Equals(item.SelectionIdentity) == true) == true;
    }

    private void Toggle(G9SelectionItem item)
    {
        if (SelectionMode == G9ChipGroupSelectionMode.SingleSelection)
        {
            if (IsSelected(item))
            {
                if (AllowNullSelection) SelectedItem = null;
            }
            else
            {
                SelectedItem = item;
            }

            RaiseSelectionChanged();
            return;
        }

        SelectedItems ??= [];
        var existing = SelectedItems.FirstOrDefault(s => s.SelectionIdentity?.Equals(item.SelectionIdentity) == true);
        if (existing is null)
        {
            SelectedItems.Add(item);
        }
        else
        {
            SelectedItems.Remove(existing);
        }

        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        IReadOnlyList<G9SelectionItem> selection = SelectionMode == G9ChipGroupSelectionMode.SingleSelection
            ? SelectedItem is null ? [] : [SelectedItem]
            : SelectedItems?.ToList() ?? [];

        SelectionChanged?.Invoke(this, selection);
    }
}
