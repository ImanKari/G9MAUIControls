using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Layouts;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;

using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Outlined trigger box that opens a search-enabled bottom-sheet picker.
///     Inherits from <see cref="G9OutlinedFieldBase" />. Single-selection mode shows a
///     value label inside the box; multi-selection mode shows a chip strip with a "+N"
///     overflow chip.
///     // TODO (palette step): chip / outline colors are inherited from the base.
/// </summary>
public partial class G9ComboBox : G9OutlinedFieldBase
{
    private readonly FlexLayout _selectionHost;
    private ObservableCollection<G9SelectionItem>? _attachedItems;
    private ObservableCollection<G9SelectionItem>? _attachedSelected;
    private bool _isOpening;
    private bool _userClearTrailing;

    /// <summary>
    ///     What the trigger content was last BUILT for (see <see cref="BuildTriggerSignature" />).
    ///     <see cref="OnRefresh" /> runs on every apply pass and every palette flip; the views are
    ///     rebuilt only when this changes, and recoloured in place otherwise (G9Controls.md §12).
    /// </summary>
    private string? _triggerSignature;

    /// <summary>The palette colours the built views currently carry.</summary>
    private TriggerColors _triggerColors;

    /// <summary>Re-applies palette colours to the views built by the last <see cref="RebuildTrigger" />.</summary>
    private readonly List<Action<G9Palette>> _recolorTrigger = [];

    [AutoBindable(OnChanged = nameof(OnItemsSourceChanged))]
    private ObservableCollection<G9SelectionItem>? _itemsSource;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnVisualChanged))]
    private G9SelectionItem? _selectedItem;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnSelectedItemsChanged))]
    private ObservableCollection<G9SelectionItem>? _selectedItems;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _allowMultipleSelection;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _sheetTitle;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _emptyStateText;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _clearButton;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9TextInputDirection _valueTextDirection;

    public G9ComboBox()
    {
        _selectionHost = new FlexLayout
        {
            AlignItems = FlexAlignItems.Center,
            Wrap = FlexWrap.NoWrap,
            Direction = FlexDirection.Row,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };

        InnerContentHost.Content = _selectionHost;

        // Default trailing icon is the search glyph; the base swaps it to "Close" when
        // ResolveTrailingIcon picks up the clear-button state below.
        TrailingIcon = G9Glyphs.Search;

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        Box.GestureRecognizers.Add(tap);

        SelectedItems = [];
        EmptyStateText = G9Strings.Get(G9StringKey.NoResults);
        ClearButton = true;
        ValueTextDirection = G9TextInputDirection.MatchParent;

        // The collections belong to the CONSUMER and usually outlive this control (a shared lookup
        // list on a singleton view-model). A CollectionChanged subscription held for the control's
        // whole life therefore kept the control — and the page it sits on — alive for as long as
        // the list. It is held only while the control is in the live tree.
        Loaded += OnComboLoaded;
        Unloaded += OnComboUnloaded;
    }

    protected override View BuildInnerContent() => _selectionHost;

    /// <summary>
    ///     No extra vertical padding — centering is handled by VerticalOptions on the host.
    /// </summary>
    protected override Thickness InnerContentPadding => new(0);

    protected override bool IsValueFloated
    {
        get
        {
            if (AllowMultipleSelection) return (SelectedItems?.Count ?? 0) > 0;
            return SelectedItem is not null;
        }
    }

    protected override bool HasExtraTrailingAffordance() => ClearButton && IsValueFloated;

    protected override View? ResolveTrailingIcon(Color stateColor)
    {
        if (ClearButton && IsValueFloated)
        {
            return G9IconFactory.Create(null, G9Glyphs.Clear, null, null, stateColor, G9Metrics.InputIconSize);
        }
        return null;
    }

    protected override string? ResolveTrailingIconSignature(Color stateColor)
    {
        if (ClearButton && IsValueFloated)
        {
            // Identity only — NOT the colour. With the colour in it, every focus change and palette
            // flip read as "a different icon" and the base tore the clear glyph down and rebuilt
            // it; with a stable signature the base recolours the existing G9IconView in place.
            return "clear";
        }
        return null;
    }

    protected override void OnTrailingTap()
    {
        if (ClearButton && IsValueFloated)
        {
            _userClearTrailing = true;
            try
            {
                if (AllowMultipleSelection)
                {
                    SelectedItems?.Clear();
                }
                else
                {
                    SelectedItem = null;
                }
            }
            finally
            {
                _userClearTrailing = false;
            }
            return;
        }

        base.OnTrailingTap();
    }

    private void OnVisualChanged() => RequestVisualUpdate();

    private void OnItemsSourceChanged()
    {
        if (IsLoaded) AttachCollections();
        RequestVisualUpdate();
    }

    private void OnSelectedItemsChanged()
    {
        if (IsLoaded) AttachCollections();
        RequestVisualUpdate();
    }

    private void OnComboLoaded(object? sender, EventArgs e)
    {
        AttachCollections();
        // Re-sync: whatever the collections did while we were not listening.
        RequestVisualUpdate();
    }

    private void OnComboUnloaded(object? sender, EventArgs e) => DetachCollections();

    /// <summary>Points the two subscriptions at the CURRENT collections (idempotent).</summary>
    private void AttachCollections()
    {
        if (!ReferenceEquals(_attachedItems, ItemsSource))
        {
            if (_attachedItems is not null) _attachedItems.CollectionChanged -= OnCollectionChanged;
            _attachedItems = ItemsSource;
            if (_attachedItems is not null) _attachedItems.CollectionChanged += OnCollectionChanged;
        }

        if (!ReferenceEquals(_attachedSelected, SelectedItems))
        {
            if (_attachedSelected is not null) _attachedSelected.CollectionChanged -= OnCollectionChanged;
            _attachedSelected = SelectedItems;
            if (_attachedSelected is not null) _attachedSelected.CollectionChanged += OnCollectionChanged;
        }
    }

    private void DetachCollections()
    {
        if (_attachedItems is not null) _attachedItems.CollectionChanged -= OnCollectionChanged;
        if (_attachedSelected is not null) _attachedSelected.CollectionChanged -= OnCollectionChanged;
        _attachedItems = null;
        _attachedSelected = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestVisualUpdate();

    protected override void OnRefresh()
    {
        if (_selectionHost is null) return;

        // Force vertical centering of the content host within the 52dp field.
        // Must be set every refresh because the base may reset it.
        if (InnerContentHost.VerticalOptions != LayoutOptions.Center)
            InnerContentHost.VerticalOptions = LayoutOptions.Center;

        RefreshTrigger();
    }

    /// <summary>
    ///     Rebuilds the trigger content only when what it SHOWS changed; a pass that changed nothing
    ///     structural (focus, enabled, error, a palette flip) recolours the existing views — or, when
    ///     the colours did not move either, does nothing at all. This used to clear and rebuild the
    ///     value label / icon / chips on every single apply pass.
    /// </summary>
    private void RefreshTrigger()
    {
        var palette = G9Palette.Current;
        var hasSelection = IsValueFloated;
        var contentFlow = hasSelection ? ResolveValueFlowDirection() : ResolveCultureFlowDirection();

        var signature = BuildTriggerSignature(hasSelection, contentFlow);
        var colors = TriggerColors.From(palette);

        if (signature == _triggerSignature)
        {
            if (colors != _triggerColors)
            {
                _triggerColors = colors;
                foreach (var recolor in _recolorTrigger) recolor(palette);
            }
            return;
        }

        _triggerSignature = signature;
        _triggerColors = colors;
        _recolorTrigger.Clear();
        RebuildTrigger(palette, hasSelection, contentFlow);
    }

    /// <summary>Everything the built trigger content depends on, EXCEPT palette colours.</summary>
    private string BuildTriggerSignature(bool hasSelection, FlowDirection contentFlow)
    {
        var sb = new StringBuilder(96);
        sb.Append(contentFlow == FlowDirection.RightToLeft ? 'R' : 'L');

        if (!hasSelection)
        {
            sb.Append("|none|");
            if (string.IsNullOrWhiteSpace(Label)) sb.Append(Placeholder);
            else sb.Append('\u0001'); // a Label is set: the floating label is the placeholder, nothing is built
            return sb.ToString();
        }

        if (!AllowMultipleSelection)
        {
            sb.Append("|one");
            AppendItemSignature(sb, SelectedItem);
            return sb.ToString();
        }

        var selected = SelectedItems;
        var count = selected?.Count ?? 0;
        sb.Append("|many|").Append(count.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < count && i < 2; i++) AppendItemSignature(sb, selected![i]);
        return sb.ToString();
    }

    private static void AppendItemSignature(StringBuilder sb, G9SelectionItem? item)
    {
        sb.Append('|');
        if (item is null) return;

        sb.Append(item.Text)
            .Append('\u0002')
            .Append(G9IconFactory.Signature(item.Emoji, item.Icon, item.IconPath, item.IconSource))
            .Append('\u0002')
            .Append(item.IconTintColor?.ToArgbHex(true))
            .Append('\u0002')
            .Append(item.SwatchFirstColor?.ToArgbHex(true))
            .Append('\u0002')
            .Append(item.SwatchSecondColor?.ToArgbHex(true));
    }

    private void RebuildTrigger(G9Palette palette, bool hasSelection, FlowDirection contentFlow)
    {
        ApplySelectionHostFlow(contentFlow);

        _selectionHost.Children.Clear();

        if (!hasSelection)
        {
            // Render the placeholder only when there's no Label configured. When a Label is
            // present, the base class's floating label acts as the rest-state placeholder
            // and a second one inside the box would just overlap visually.
            if (!string.IsNullOrWhiteSpace(Label))
            {
                return;
            }

            var placeholder = CreateValueLabel(Placeholder ?? string.Empty, palette.TextTertiary, contentFlow);
            _recolorTrigger.Add(p => placeholder.TextColor = p.TextTertiary);
            _selectionHost.Children.Add(placeholder);
            return;
        }

        if (!AllowMultipleSelection)
        {
            var selectedItem = SelectedItem;
            if (selectedItem is not null)
            {
                var icon = CreateValueIcon(selectedItem, palette);
                if (icon is not null)
                {
                    if (icon is G9IconView iconView)
                    {
                        _recolorTrigger.Add(p => iconView.Color = selectedItem.IconTintColor ?? p.OnSurfaceVariant);
                    }

                    _selectionHost.Children.Add(icon);
                }
            }

            var value = CreateValueLabel(SelectedItem?.Text ?? string.Empty, palette.TextPrimary, contentFlow);
            _recolorTrigger.Add(p => value.TextColor = p.TextPrimary);
            _selectionHost.Children.Add(value);
            return;
        }

        var selected = SelectedItems?.ToList() ?? [];
        foreach (var item in selected.Take(2))
        {
            _selectionHost.Children.Add(CreateSelectedChip(item, contentFlow, _recolorTrigger));
        }

        if (selected.Count > 2)
        {
            _selectionHost.Children.Add(CreateOverflowChip($"+{selected.Count - 2}", contentFlow, _recolorTrigger));
        }
    }

    /// <summary>The palette colours the trigger content paints with — compared to skip a no-op recolour.</summary>
    private readonly record struct TriggerColors(
        Color? Primary, Color? OnPrimary, Color? TextPrimary, Color? TextSecondary, Color? TextTertiary,
        Color? OnSurfaceVariant, Color? OutlineVariant, Color? SurfaceVariant)
    {
        public static TriggerColors From(G9Palette palette) => new(
            palette.Primary, palette.OnPrimary, palette.TextPrimary, palette.TextSecondary, palette.TextTertiary,
            palette.OnSurfaceVariant, palette.OutlineVariant, palette.SurfaceVariant);
    }

    private void ApplySelectionHostFlow(FlowDirection flow)
    {
        if (_selectionHost.FlowDirection != flow)
        {
            _selectionHost.FlowDirection = flow;
        }

        if (_selectionHost.JustifyContent != FlexJustify.Start)
        {
            _selectionHost.JustifyContent = FlexJustify.Start;
        }
    }

    private FlowDirection ResolveValueFlowDirection()
    {
        return ValueTextDirection switch
        {
            G9TextInputDirection.LeftToRight => FlowDirection.LeftToRight,
            G9TextInputDirection.RightToLeft => FlowDirection.RightToLeft,
            _ => ResolveCultureFlowDirection()
        };
    }

    private static FlowDirection ResolveCultureFlowDirection()
    {
        return G9Visuals.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    /// <summary>
    ///     Leading icon (or, failing that, a two-color swatch for items with only stored colors —
    ///     e.g. a variety's FirstColor/SecondColor) for the single-selected value shown in the
    ///     trigger box. Mirrors the icon rendering <see cref="CreateSelectedChip" /> already does
    ///     for multi-select chips.
    /// </summary>
    private static View? CreateValueIcon(G9SelectionItem item, G9Palette palette)
    {
        if (G9IconFactory.HasIcon(item.Emoji, item.Icon, item.IconPath, item.IconSource))
        {
            return G9IconFactory.Create(
                item.Emoji, item.Icon, item.IconPath, item.IconSource,
                item.IconTintColor ?? palette.OnSurfaceVariant, 18);
        }

        if (G9Visuals.HasSwatch(item.SwatchFirstColor, item.SwatchSecondColor))
        {
            return G9Visuals.CreateSwatch(item.SwatchFirstColor, item.SwatchSecondColor, 18);
        }

        return null;
    }

    private static Label CreateValueLabel(string text, Color textColor, FlowDirection flow)
    {
        var label = new Label
        {
            Text = text,
            TextColor = textColor,
            FontSize = 15,
            VerticalTextAlignment = TextAlignment.Center,

            // ⛔ ALWAYS Start, never a direction ternary. The label carries `flow` itself, so Start is
            // already mirrored: right under RTL, left under LTR. Picking `End` for RTL asked for the
            // physical LEFT edge of a label that had just been told to read right-to-left — so the
            // value text was pushed to the far side of the trigger while its own icon stayed on the
            // reading edge, with the whole field's width between them. Same defect class as the one
            // G9CultureDateTimeLabel shipped in 1.0.1: express alignment LOGICALLY and let the flow
            // direction resolve it (see the consuming app's design guide §4z).
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = flow,
            LineBreakMode = LineBreakMode.TailTruncation,

            // ⛔ Start, and NOT Fill. The host is a FlexLayout whose main-axis direction is Row; how it
            // resolves that against FlowDirection is not something a label should be betting on. With
            // Fill + Grow the label claimed all the leftover width, so which EDGE of the trigger the
            // value landed on depended on that resolution — and the value drifted away from its own
            // leading icon, with the width of the field between them. Sized to its text and packed at
            // the host's start, the glyph and its label are one block on the reading edge whatever the
            // flex does. Shrink is kept so a long value still truncates instead of pushing the icon out.
            HorizontalOptions = LayoutOptions.Start,
            MaxLines = 1
        };

        FlexLayout.SetGrow(label, 0);
        FlexLayout.SetShrink(label, 1);
        return label;
    }

    private static View CreateSelectedChip(G9SelectionItem item, FlowDirection flow, List<Action<G9Palette>> recolor)
    {
        var palette = G9Palette.Current;
        var row = new HorizontalStackLayout
        {
            Spacing = 6,
            FlowDirection = flow,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        if (G9IconFactory.HasIcon(item.Emoji, item.Icon, item.IconPath, item.IconSource))
        {
            var icon = G9IconFactory.Create(
                item.Emoji,
                item.Icon,
                item.IconPath,
                item.IconSource,
                palette.OnPrimary,
                14,
                4);
            if (icon is G9IconView iconView) recolor.Add(p => iconView.Color = p.OnPrimary);
            row.Children.Add(icon);
        }

        var label = new Label
        {
            Text = item.Text,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = palette.OnPrimary,
            FlowDirection = flow,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.Center
        };
        row.Children.Add(label);

        var stroke = new SolidColorBrush(palette.Primary);
        var chip = new Border
        {
            FlowDirection = flow,
            HeightRequest = 34,
            MinimumHeightRequest = 34,
            StrokeThickness = 1.2,
            Stroke = stroke,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusXs),
            Background = G9Colors.BuildSolidOrGradient(palette.Primary, useGradient: true),
            Margin = flow == FlowDirection.RightToLeft ? new Thickness(6, 0, 0, 0) : new Thickness(0, 0, 6, 0),
            Padding = new Thickness(12, 0),
            VerticalOptions = LayoutOptions.Center,
            Content = row
        };

        // Runs only when the palette actually changed (RefreshTrigger compares the colours first),
        // so the one gradient brush allocated here is per theme flip, not per apply pass.
        recolor.Add(p =>
        {
            label.TextColor = p.OnPrimary;
            stroke.Color = p.Primary;
            chip.Background = G9Colors.BuildSolidOrGradient(p.Primary, useGradient: true);
        });

        return chip;
    }

    private static View CreateOverflowChip(string text, FlowDirection flow, List<Action<G9Palette>> recolor)
    {
        var palette = G9Palette.Current;
        var label = new Label
        {
            Text = text,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = palette.TextSecondary,
            FlowDirection = flow,
            LineBreakMode = LineBreakMode.NoWrap,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.Center
        };

        var stroke = new SolidColorBrush(palette.OutlineVariant);
        var chip = new Border
        {
            FlowDirection = flow,
            HeightRequest = 26,
            MinimumHeightRequest = 26,
            StrokeThickness = 1,
            Stroke = stroke,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusPill),
            BackgroundColor = palette.SurfaceVariant,
            Margin = flow == FlowDirection.RightToLeft ? new Thickness(6, 0, 0, 0) : new Thickness(0, 0, 6, 0),
            Padding = new Thickness(9, 0),
            VerticalOptions = LayoutOptions.Center,
            Content = label
        };

        recolor.Add(p =>
        {
            label.TextColor = p.TextSecondary;
            stroke.Color = p.OutlineVariant;
            chip.BackgroundColor = p.SurfaceVariant;
        });

        return chip;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || IsReadOnly || _isOpening || _userClearTrailing) return;

        List<G9SelectionItem> selected = AllowMultipleSelection
            ? SelectedItems?.ToList() ?? []
            : SelectedItem is null ? [] : [SelectedItem];

        // Disabled items stay out of the list — EXCEPT the ones currently selected. The current
        // selection must always be a candidate: filtered out, the sheet could not show it, could
        // not return it, and the write-back below cleared it (a disabled value was nulled by
        // simply opening and dismissing the sheet). It shows as a dimmed, non-tappable row.
        var items = ItemsSource?.Where(i => i.IsEnabled || selected.Contains(i)).ToList() ?? [];
        if (items.Count == 0) return;

        _isOpening = true;
        try
        {
            this.Unfocus();

            var title = string.IsNullOrWhiteSpace(SheetTitle)
                ? Label ?? Placeholder ?? string.Empty
                : SheetTitle!;

            var result = await G9SelectionSheet.ShowForResultAsync(
                title, items, selected,
                AllowMultipleSelection,
                closeOnSingleSelection: !AllowMultipleSelection,
                showSearch: true,
                EmptyStateText,
                ValueTextDirection).ConfigureAwait(true);

            if (AllowMultipleSelection)
            {
                // Done commits; so does dismissing a sheet whose selection was changed (that is how
                // this control has always behaved). A sheet dismissed untouched — or closed before
                // its content was even built — writes NOTHING back: it used to come back as an
                // empty list, which cleared the bound collection.
                if (!result.WasAccepted && !result.SelectionChanged) return;

                SelectedItems ??= [];
                if (SelectedItems.SequenceEqual(result.Items)) return;

                SelectedItems.Clear();
                foreach (var item in result.Items) SelectedItems.Add(item);
            }
            else if (result.WasAccepted)
            {
                // Only a PICK writes back. A dismissal leaves SelectedItem exactly as it was.
                SelectedItem = result.Items.FirstOrDefault();
            }
        }
        finally
        {
            _isOpening = false;
        }
    }
}
