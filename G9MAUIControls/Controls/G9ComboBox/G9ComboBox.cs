using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Layouts;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

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
            return $"clear|{stateColor.ToArgbHex()}";
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
        if (_attachedItems is not null) _attachedItems.CollectionChanged -= OnCollectionChanged;
        _attachedItems = ItemsSource;
        if (_attachedItems is not null) _attachedItems.CollectionChanged += OnCollectionChanged;
        RequestVisualUpdate();
    }

    private void OnSelectedItemsChanged()
    {
        if (_attachedSelected is not null) _attachedSelected.CollectionChanged -= OnCollectionChanged;
        _attachedSelected = SelectedItems;
        if (_attachedSelected is not null) _attachedSelected.CollectionChanged += OnCollectionChanged;
        RequestVisualUpdate();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestVisualUpdate();

    protected override void OnRefresh()
    {
        if (_selectionHost is null) return;

        // Force vertical centering of the content host within the 52dp field.
        // Must be set every refresh because the base may reset it.
        if (InnerContentHost.VerticalOptions != LayoutOptions.Center)
            InnerContentHost.VerticalOptions = LayoutOptions.Center;

        RebuildTrigger();
    }

    private void RebuildTrigger()
    {
        var palette = G9Palette.Current;
        var hasSelection = IsValueFloated;
        var contentFlow = hasSelection ? ResolveValueFlowDirection() : ResolveCultureFlowDirection();
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

            _selectionHost.Children.Add(CreateValueLabel(Placeholder ?? string.Empty, palette.TextTertiary, contentFlow));
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
                    _selectionHost.Children.Add(icon);
                }
            }

            _selectionHost.Children.Add(CreateValueLabel(SelectedItem?.Text ?? string.Empty, palette.TextPrimary, contentFlow));
            return;
        }

        var selected = SelectedItems?.ToList() ?? [];
        foreach (var item in selected.Take(2))
        {
            _selectionHost.Children.Add(CreateSelectedChip(item, contentFlow));
        }

        if (selected.Count > 2)
        {
            _selectionHost.Children.Add(CreateOverflowChip($"+{selected.Count - 2}", contentFlow));
        }
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

    private static View CreateSelectedChip(G9SelectionItem item, FlowDirection flow)
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
            row.Children.Add(G9IconFactory.Create(
                item.Emoji,
                item.Icon,
                item.IconPath,
                item.IconSource,
                palette.OnPrimary,
                14,
                4));
        }

        row.Children.Add(new Label
        {
            Text = item.Text,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = palette.OnPrimary,
            FlowDirection = flow,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.Center
        });

        return new Border
        {
            FlowDirection = flow,
            HeightRequest = 34,
            MinimumHeightRequest = 34,
            StrokeThickness = 1.2,
            Stroke = new SolidColorBrush(palette.Primary),
            StrokeShape = G9Colors.Round(G9Metrics.RadiusXs),
            Background = G9Colors.BuildSolidOrGradient(palette.Primary, useGradient: true),
            Margin = flow == FlowDirection.RightToLeft ? new Thickness(6, 0, 0, 0) : new Thickness(0, 0, 6, 0),
            Padding = new Thickness(12, 0),
            VerticalOptions = LayoutOptions.Center,
            Content = row
        };
    }

    private static View CreateOverflowChip(string text, FlowDirection flow)
    {
        var palette = G9Palette.Current;
        return new Border
        {
            FlowDirection = flow,
            HeightRequest = 26,
            MinimumHeightRequest = 26,
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(palette.OutlineVariant),
            StrokeShape = G9Colors.Round(G9Metrics.RadiusPill),
            BackgroundColor = palette.SurfaceVariant,
            Margin = flow == FlowDirection.RightToLeft ? new Thickness(6, 0, 0, 0) : new Thickness(0, 0, 6, 0),
            Padding = new Thickness(9, 0),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = palette.TextSecondary,
                FlowDirection = flow,
                LineBreakMode = LineBreakMode.NoWrap,
                MaxLines = 1,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || IsReadOnly || _isOpening || _userClearTrailing) return;

        var items = ItemsSource?.Where(i => i.IsEnabled).ToList() ?? [];
        if (items.Count == 0) return;

        _isOpening = true;
        try
        {
            this.Unfocus();

            var selected = AllowMultipleSelection
                ? SelectedItems?.ToList() ?? []
                : SelectedItem is null ? [] : [SelectedItem];

            var title = string.IsNullOrWhiteSpace(SheetTitle)
                ? Label ?? Placeholder ?? string.Empty
                : SheetTitle!;

            var result = await G9SelectionSheet.ShowAsync(
                title, items, selected,
                AllowMultipleSelection,
                closeOnSingleSelection: !AllowMultipleSelection,
                showSearch: true,
                EmptyStateText,
                ValueTextDirection).ConfigureAwait(true);

            if (AllowMultipleSelection)
            {
                SelectedItems ??= [];
                SelectedItems.Clear();
                foreach (var item in result) SelectedItems.Add(item);
            }
            else
            {
                SelectedItem = result.FirstOrDefault();
            }
        }
        finally
        {
            _isOpening = false;
        }
    }
}
