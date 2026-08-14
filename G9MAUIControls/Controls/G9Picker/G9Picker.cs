using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;

using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Outlined trigger box that opens a single-selection bottom-sheet picker.
///     Inherits the shared outline + notched-label architecture from
///     <see cref="G9OutlinedFieldBase" />. Trailing chevron is always rendered through the
///     base's built-in trailing icon path (set via <see cref="G9OutlinedFieldBase.TrailingIcon" />)
///     so a single tap behaviour applies.
///     // TODO (palette step): outline / chevron colors are inherited from the base.
/// </summary>
public partial class G9Picker : G9OutlinedFieldBase
{
    private readonly Label _valueLabel;
    private readonly HorizontalStackLayout _content;
    private View? _valueIcon;
    private ObservableCollection<G9SelectionItem>? _attachedItems;
    private bool _isOpening;

    [AutoBindable(OnChanged = nameof(OnItemsSourceChanged))]
    private ObservableCollection<G9SelectionItem>? _itemsSource;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnSelectedItemChanged))]
    private G9SelectionItem? _selectedItem;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _sheetTitle;
    [AutoBindable] private bool _restoreOnCancel;
    [AutoBindable] private ICommand? _selectionAcceptedCommand;
    [AutoBindable] private ICommand? _selectionCancelledCommand;

    public G9Picker()
    {
        _valueLabel = new Label
        {
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Fill
        };

        _content = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { _valueLabel }
        };

        TrailingIcon = G9Glyphs.Chevron;

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        Box.GestureRecognizers.Add(tap);

        RestoreOnCancel = true;
    }

    public event EventHandler<G9SelectionItem?>? ItemSelected;
    public event EventHandler? SelectionCancelled;

    protected override View BuildInnerContent() => _content;

    protected override bool IsValueFloated => SelectedItem is not null;

    private void OnVisualChanged() => RequestVisualUpdate();

    private void OnItemsSourceChanged()
    {
        if (_attachedItems is not null) _attachedItems.CollectionChanged -= OnCollectionChanged;
        _attachedItems = ItemsSource;
        if (_attachedItems is not null) _attachedItems.CollectionChanged += OnCollectionChanged;
        RequestVisualUpdate();
    }

    private void OnSelectedItemChanged() => RequestVisualUpdate();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestVisualUpdate();

    protected override void OnRefresh()
    {
        if (_valueLabel is null) return;

        var palette = G9Palette.Current;
        var hasValue = SelectedItem is not null;

        if (hasValue)
        {
            _valueLabel.Text = SelectedItem!.Text;
            _valueLabel.TextColor = palette.TextPrimary;
        }
        else if (string.IsNullOrWhiteSpace(Label))
        {
            // No floating label is configured, so the inner value label is the placeholder.
            _valueLabel.Text = Placeholder ?? string.Empty;
            _valueLabel.TextColor = palette.TextTertiary;
        }
        else
        {
            // The base class's floating label is acting as the placeholder in rest state —
            // don't render a second placeholder text inside the box.
            _valueLabel.Text = string.Empty;
        }

        _valueLabel.HorizontalTextAlignment = G9Visuals.IsRtl ? TextAlignment.End : TextAlignment.Start;

        // _content sits inside G9OutlinedFieldBase's Box, which is HARD-LOCKED to
        // FlowDirection.LeftToRight (see that class — the icon/trailing columns are
        // re-mapped physically instead). Without this explicit override, _content silently
        // INHERITS that locked LTR regardless of app culture, so the icon+text group always
        // packs against the physical-left edge — the RTL "value renders on the wrong side"
        // bug. Set it explicitly every refresh, mirroring G9ComboBox's _selectionHost.
        var flow = G9Visuals.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (_content.FlowDirection != flow)
        {
            _content.FlowDirection = flow;
        }

        RefreshValueIcon(hasValue ? SelectedItem : null);
    }

    /// <summary>
    ///     Shows the selected item's icon (Emoji/Icon/IconPath/IconSource) or, failing
    ///     that, a two-color swatch (e.g. a variety's stored colors) leading the value text —
    ///     mirrors the icon rendering <see cref="G9ComboBox" /> already does for its chips.
    /// </summary>
    private void RefreshValueIcon(G9SelectionItem? item)
    {
        if (_valueIcon is not null)
        {
            _content.Children.Remove(_valueIcon);
            _valueIcon = null;
        }

        if (item is null)
        {
            return;
        }

        var palette = G9Palette.Current;
        View? icon = null;

        if (G9IconFactory.HasIcon(item.Emoji, item.Icon, item.IconPath, item.IconSource))
        {
            icon = G9IconFactory.Create(
                item.Emoji, item.Icon, item.IconPath, item.IconSource,
                item.IconTintColor ?? palette.OnSurfaceVariant, 18);
        }
        else if (G9Visuals.HasSwatch(item.SwatchFirstColor, item.SwatchSecondColor))
        {
            icon = G9Visuals.CreateSwatch(item.SwatchFirstColor, item.SwatchSecondColor, 18);
        }

        if (icon is null)
        {
            return;
        }

        _valueIcon = icon;
        _content.Children.Insert(0, icon);
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || IsReadOnly || _isOpening) return;

        var items = ItemsSource?.Where(i => i.IsEnabled).ToList() ?? [];
        if (items.Count == 0) return;

        _isOpening = true;
        try
        {
            this.Unfocus();

            var previous = SelectedItem;
            var title = string.IsNullOrWhiteSpace(SheetTitle)
                ? Label ?? Placeholder ?? string.Empty
                : SheetTitle!;

            var result = await G9SelectionSheet.ShowAsync(
                title, items, previous is null ? null : [previous],
                allowMultiple: false,
                closeOnSingleSelection: true,
                showSearch: false).ConfigureAwait(true);

            var next = result.FirstOrDefault();
            if (next is not null)
            {
                SelectedItem = next;
                ItemSelected?.Invoke(this, next);
                if (SelectionAcceptedCommand?.CanExecute(next) == true) SelectionAcceptedCommand.Execute(next);
            }
            else if (RestoreOnCancel)
            {
                SelectedItem = previous;
                SelectionCancelled?.Invoke(this, EventArgs.Empty);
                if (SelectionCancelledCommand?.CanExecute(previous) == true) SelectionCancelledCommand.Execute(previous);
            }
        }
        finally
        {
            _isOpening = false;
        }
    }
}
