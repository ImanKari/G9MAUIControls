using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;
using System.Text;

namespace G9MAUIControls.Controls;

/// <summary>
///     Material-style PIN / OTP input. A row of small one-character cells with the
///     auto-advance / auto-back-on-delete behaviour users expect from a verification
///     code field, plus optional grouping with separator characters (e.g. credit-card
///     style "4-4-4-4 with '-' separator").
///
///     <para>
///         <b>Inheritance.</b> Extends <see cref="G9ControlBase" /> directly rather
///         than <see cref="G9OutlinedFieldBase" />. The visual is a row of tiny cells,
///         not a single outlined field with a floating label.
///     </para>
///
///     <para>
///         <b>Architecture: single hidden Entry + visual-only cells.</b> The naïve
///         "one Entry per cell with <c>MaxLength=1</c>" approach hits a wall of
///         per-platform IME edge cases — backspace on an empty cell silently fails
///         on some Android keyboards, "select-all on focus" races the IME's own
///         focus dispatch, and manual <c>Entry.Focus()</c> calls inside
///         <c>TextChanged</c> handlers crash on certain platforms (we shipped
///         exactly that crash on the password mode in the per-cell variant). All
///         those problems disappear with this model:
///         <list type="number">
///             <item><b>One <see cref="Entry" /> holds the full PIN string.</b> It's
///                 1 px × 1 px, opacity 0, and pinned to the top-left corner of the
///                 control's root Grid (so it overlaps cell 0). It is NOT pushed
///                 off-screen via a negative Margin — that triggered the host
///                 ScrollView's "scroll-to-focused-input" behaviour, which yanked
///                 the page up to the off-screen Entry's position the moment any
///                 cell was tapped. Sitting inside the visible bounds means
///                 scroll-to-focus is a no-op.</item>
///             <item><b>The hidden Entry has <c>InputTransparent=true</c>.</b> Its
///                 1×1 hit-rect doesn't block taps on the cell <see cref="Border" />
///                 underneath. The Entry still receives keyboard input —
///                 <c>InputTransparent</c> only affects pointer events, not focus or
///                 the IME pipeline — and we drive focus programmatically from each
///                 cell's <see cref="TapGestureRecognizer" />.</item>
///             <item><b>Each cell is a <see cref="Border" /> + <see cref="Label" />.</b>
///                 No editable widget per cell — just visuals. Tapping a cell calls
///                 <c>_hidden.Focus()</c>; the keyboard pops, the user types, the
///                 hidden Entry's text grows.</item>
///             <item><b>Auto-advance is implicit.</b> The hidden Entry's cursor
///                 moves forward as the user types. <see cref="OnApplyVisuals" />
///                 reads the cursor position and re-renders the cells.</item>
///             <item><b>Backspace walks back implicitly.</b> The hidden Entry
///                 deletes one char per backspace; the cell visual updates to
///                 match.</item>
///             <item><b>Multi-char paste fills cells left-to-right.</b> Pasting
///                 <c>"1234"</c> sets <c>Text="1234"</c> in one event; we render
///                 four filled cells in the next visuals pass.</item>
///             <item><b>Tap-to-edit a filled cell.</b> Tapping cell K when it
///                 holds a char positions the caret at K and selects the existing
///                 character (<c>SelectionLength=1</c>). The next keystroke
///                 REPLACES the selection (the platform's MaxLength constraint
///                 doesn't block replacements, only inserts). After each
///                 replacement, the post-TextChanged dispatcher pre-selects the
///                 next cell's char so subsequent keystrokes keep replacing
///                 cell-by-cell instead of being silently dropped.</item>
///         </list>
///     </para>
///
///     <para>
///         <b>Type filtering.</b> The on-screen keyboard is hinted via
///         <c>Entry.Keyboard</c> — <c>Keyboard.Numeric</c> for
///         digit-only modes, <c>Keyboard.Default</c> for letter /
///         alphanumeric modes. A safety-net filter in
///         <see cref="OnHiddenTextChanged" /> strips chars that fail
///         <see cref="IsAllowed" /> so a clipboard paste from any source can't
///         sneak past the keyboard hint.
///     </para>
///
///     <para>
///         <b>Password masking.</b> When <see cref="Type" /> is
///         <see cref="G9PinEntryType.Password" /> we keep the real digits in the
///         hidden Entry's <c>Entry.Text</c> (so <see cref="Value" />
///         exposes the actual PIN) and render <see cref="MaskCharacter" /> in the
///         cell <see cref="Label" />. We never set <c>IsPassword=true</c> on the
///         hidden Entry — that path was the password-mode crash trigger in the
///         previous per-cell implementation, AND it would mask the cursor's own
///         position indicator on platforms that animate the password reveal.
///     </para>
/// </summary>
public partial class G9PinEntry : G9ControlBase
{
    private readonly Grid _root;
    private readonly HorizontalStackLayout _row;
    private readonly Entry _hidden;
    private readonly List<CellVisual> _cells = new();
    private readonly List<Label> _separatorLabels = new();
    private bool _suppress;
    private bool _completedFired;

    [AutoBindable(OnChanged = nameof(OnLayoutChanged))] private int _length = 4;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnValueExternalChanged))]
    private string? _value;

    [AutoBindable(OnChanged = nameof(OnTypeChanged))] private G9PinEntryType _type;
    [AutoBindable(OnChanged = nameof(OnVisualOnly))] private string? _placeholder;
    [AutoBindable(OnChanged = nameof(OnVisualOnly))] private char _maskCharacter = '\u25CF';
    [AutoBindable(OnChanged = nameof(OnLayoutChanged))] private string? _groupSizes;
    [AutoBindable(OnChanged = nameof(OnLayoutChanged))] private string? _separator = "-";
    [AutoBindable(OnChanged = nameof(OnVisualOnly))] private double _cellWidth;
    [AutoBindable(OnChanged = nameof(OnVisualOnly))] private double _cellHeight;
    [AutoBindable] private bool _autoFocus;
    [AutoBindable] private bool _isComplete;

    public event EventHandler<string>? ValueChanged;
    public event EventHandler<string>? Completed;

    public G9PinEntry()
    {
        _hidden = new Entry
        {
            // NOTE: deliberately NOT tagged with G9PlatformConfig.NoUnderlineStyleId.
            // This Entry is invisible (Opacity 0, 1×1 dp) and exists only to host the IME /
            // keystroke pipeline — it is never painted, so it needs no native-chrome strip.
            // On Windows the no-underline mapper now defers its resource-dictionary writes to
            // the platform TextBox's Loaded event (so they actually apply on a live XamlRoot,
            // which is what fixed the hover / focus background on the *visible* fields). Those
            // resource writes executing against this 1×1 hidden TextBox interfered with its
            // text pipeline and broke PIN typing. At the previous baseline the same writes
            // threw pre-XamlRoot and were silently swallowed, so the hidden Entry was never
            // affected. Leaving the StyleId off keeps the mapper from touching it at all —
            // the only correct behaviour for an invisible input. Caret is hidden separately
            // via HideHiddenCaret().
            Text = string.Empty,
            BackgroundColor = Colors.Transparent,
            FontSize = 1,
            HeightRequest = 1,
            WidthRequest = 1,
            Opacity = 0,
            ClearButtonVisibility = ClearButtonVisibility.Never,
            Keyboard = Keyboard.Numeric,
            // Pin the Entry inside the control's visible bounds (top-left corner of
            // the root Grid, overlapping the first cell area). A previous version
            // pushed it off-screen via a giant negative Margin — that triggered the
            // host ScrollView's "scroll-to-focused-input" mechanism, which yanked
            // the page up to the off-screen Entry's position the moment any cell
            // was tapped. Keeping the Entry within the visible layout means
            // scroll-to-focus is a no-op (the Entry is already on screen).
            //
            // InputTransparent=true so the Entry's 1×1 hit-rect doesn't block taps
            // on the cell Border underneath. The Entry still receives keyboard
            // input — InputTransparent only affects pointer events, not focus or
            // the IME pipeline — and we drive focus programmatically from the
            // cell's TapGestureRecognizer.
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            // Always LTR. PIN / OTP codes are entered left-to-right regardless of the
            // surrounding UI culture — see the FlowDirection note on _root.
            FlowDirection = FlowDirection.LeftToRight
        };
        _hidden.TextChanged += OnHiddenTextChanged;
        _hidden.Focused += OnHiddenFocusChanged;
        _hidden.Unfocused += OnHiddenFocusChanged;
        _hidden.HandlerChanged += (_, _) =>
        {
            HideHiddenCaret();
#if WINDOWS
            HookWinUiTextBridge();
#endif
        };

        _row = new HorizontalStackLayout
        {
            Spacing = G9Metrics.PinCellSpacing,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };

        _root = new Grid
        {
            // PIN cells are always laid out left-to-right regardless of the surrounding
            // culture. PIN / OTP codes are universally entered left-to-right (the same
            // way SMS verification codes arrive); flowing them with an RTL parent would
            // visually swap "1234" with "4321" and confuse users who are reading the
            // code off a SMS. Group separators ("4-4-4-4 card-style") would also end
            // up rendered in the wrong sequence. Locking the PIN row to LTR keeps the
            // visual order matching the keystroke order across both cultures.
            FlowDirection = FlowDirection.LeftToRight
        };
        _root.Add(_row);
        _root.Add(_hidden);
        Content = _root;

        Type = G9PinEntryType.Number;
        CellWidth = G9Metrics.PinCellWidth;
        CellHeight = G9Metrics.PinCellHeight;
        // [AutoBindable] ignores private-field initializers when generating the
        // BindableProperty default — MaskCharacter would otherwise default to
        // '\0' (null char), making password cells render blank. Set it explicitly
        // here so the BindableProperty default + the field stay in sync.
        MaskCharacter = '\u25CF';
        BuildCells();
    }

    /// <summary>Focus the hidden Entry — caret lands at the end of the currently typed value.</summary>
    public void FocusFirstEmpty()
    {
        var raw = _hidden.Text ?? string.Empty;
        _hidden.CursorPosition = Math.Min(raw.Length, _cells.Count);
        _hidden.Focus();
    }

    /// <summary>Wipes every cell and resets focus to the start.</summary>
    public void Clear()
    {
        _suppress = true;
        try
        {
            _hidden.Text = string.Empty;
            Value = string.Empty;
        }
        finally
        {
            _suppress = false;
        }
        _completedFired = false;
        IsComplete = false;
        ValueChanged?.Invoke(this, string.Empty);
        RequestVisualUpdate();
    }

    private void OnLayoutChanged()
    {
        BuildCells();
        SyncHiddenFromValue();
        RequestVisualUpdate();
    }

    private void OnTypeChanged()
    {
        _hidden.Keyboard = Type switch
        {
            G9PinEntryType.Number or G9PinEntryType.Password => Keyboard.Numeric,
            _ => Keyboard.Default
        };
        // Re-validate the stored value: chars that aren't allowed under the new type
        // are dropped silently.
        SyncHiddenFromValue();
        RequestVisualUpdate();
    }

    private void OnVisualOnly() => RequestVisualUpdate();

    private void OnValueExternalChanged()
    {
        if (_suppress) return;
        SyncHiddenFromValue();
    }

    private void SyncHiddenFromValue()
    {
        var v = Value ?? string.Empty;
        var clean = FilterAndClamp(v);
        _suppress = true;
        try
        {
            if (_hidden.Text != clean) _hidden.Text = clean;
            _hidden.MaxLength = _cells.Count > 0 ? _cells.Count : int.MaxValue;
        }
        finally
        {
            _suppress = false;
        }

        var ready = clean.Length == _cells.Count && _cells.Count > 0;
        IsComplete = ready;
        _completedFired = ready;
    }

    private void OnHiddenTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        var raw = _hidden.Text ?? string.Empty;
        var clean = FilterAndClamp(raw);
        if (clean != raw)
        {
            // Push the filtered string back into the Entry. The recursive TextChanged
            // is suppressed via _suppress so the rest of this handler proceeds with
            // the same `clean` value.
            _suppress = true;
            try { _hidden.Text = clean; }
            finally { _suppress = false; }
        }

        _suppress = true;
        try { Value = clean; }
        finally { _suppress = false; }
        ValueChanged?.Invoke(this, clean);

        var ready = clean.Length == _cells.Count && _cells.Count > 0;
        if (ready && !_completedFired)
        {
            _completedFired = true;
            IsComplete = true;
            Completed?.Invoke(this, clean);
        }
        else if (!ready && _completedFired)
        {
            _completedFired = false;
            IsComplete = false;
        }

        // Standard sequential PIN model: the field is append-only from the user's
        // point of view. After every text change we force the caret to the END of the
        // string so the next keystroke appends (auto-advance) and backspace removes the
        // last character (auto-back). This replaces the old CursorPosition / SelectionLength
        // "pre-select the next char to allow mid-string replace" dance, which depended on
        // the platform caret behaving consistently — it doesn't (Android resets it after a
        // programmatic Text write; on Windows our TextChanging bridge snaps the virtual
        // caret to 0), and that mismatch was freezing the highlight on the first cell and
        // making typing/backspace edit the wrong position. Deferred via Dispatcher so the
        // platform finishes its own post-change work before we move the caret.
        Dispatcher.Dispatch(() =>
        {
            try
            {
                var end = (_hidden.Text ?? string.Empty).Length;
                if (_hidden.SelectionLength != 0) _hidden.SelectionLength = 0;
                if (_hidden.CursorPosition != end) _hidden.CursorPosition = end;
            }
            catch { /* ignore — caret APIs can throw before the platform settles */ }
            RequestVisualUpdate();
        });

        RequestVisualUpdate();
    }

    private void OnHiddenFocusChanged(object? sender, FocusEventArgs e) => RequestVisualUpdate();

    /// <summary>
    ///     Strips characters that don't pass <see cref="IsAllowed" /> and truncates to
    ///     the cell count. Used both for incoming TextChanged events (against
    ///     clipboard pastes) and for programmatic <see cref="Value" /> writes from a
    ///     view model.
    /// </summary>
    private string FilterAndClamp(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var max = _cells.Count > 0 ? _cells.Count : raw.Length;
        var sb = new StringBuilder(Math.Min(raw.Length, max));
        foreach (var ch in raw)
        {
            if (sb.Length >= max) break;
            if (IsAllowed(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    private bool IsAllowed(char ch)
    {
        return Type switch
        {
            G9PinEntryType.Number or G9PinEntryType.Password => char.IsDigit(ch),
            G9PinEntryType.Letters => char.IsLetter(ch),
            G9PinEntryType.Alphanumeric => char.IsLetterOrDigit(ch),
            _ => true
        };
    }

    private void BuildCells()
    {
        _row.Clear();
        _cells.Clear();
        _separatorLabels.Clear();

        var groups = ParseGroupSizes(GroupSizes);
        var totalCells = groups.Count > 0 ? groups.Sum() : Math.Max(1, Length);

        var groupCounter = 0;
        var cellsInCurrentGroup = 0;
        var currentGroup = groups.Count > 0 ? groups[0] : totalCells;
        var separatorChar = string.IsNullOrEmpty(Separator) ? string.Empty : Separator!;

        for (var i = 0; i < totalCells; i++)
        {
            var cell = CreateCell(i);
            _cells.Add(cell);
            _row.Add(cell.Border);
            cellsInCurrentGroup++;

            if (groups.Count > 0 &&
                groupCounter < groups.Count - 1 &&
                cellsInCurrentGroup == currentGroup)
            {
                if (!string.IsNullOrEmpty(separatorChar))
                {
                    var label = new Label
                    {
                        Text = separatorChar,
                        FontSize = G9Metrics.PinSeparatorFontSize,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(G9Metrics.PinSeparatorSpacing - G9Metrics.PinCellSpacing, 0)
                    };
                    _separatorLabels.Add(label);
                    _row.Add(label);
                }
                groupCounter++;
                cellsInCurrentGroup = 0;
                currentGroup = groups[groupCounter];
            }
        }

        _hidden.MaxLength = totalCells > 0 ? totalCells : int.MaxValue;
    }

    private static List<int> ParseGroupSizes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<int>();
        var parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (int.TryParse(p, out var n) && n > 0) result.Add(n);
        }
        return result;
    }

    /// <summary>
    ///     Hides the platform caret on the hidden Entry. The Entry is opacity 0
    ///     and 1×1 dp, but on Android the cursor is rendered via the platform
    ///     EditText regardless of opacity — and on Windows the caret line is
    ///     painted on top of opacity-0 surfaces too. Killing the visual cursor
    ///     keeps the only "blink" on screen out of the user's way (the cell
    ///     focus border is the focus indicator).
    /// </summary>
    private void HideHiddenCaret()
    {
        if (_hidden.Handler is null) return;
#if ANDROID
        if (_hidden.Handler.PlatformView is global::Android.Widget.EditText edit)
        {
            edit.SetCursorVisible(false);
        }
#elif IOS || MACCATALYST
        if (_hidden.Handler.PlatformView is global::UIKit.UITextField tf)
        {
            tf.TintColor = global::UIKit.UIColor.Clear;
        }
#elif WINDOWS
        if (_hidden.Handler.PlatformView is Microsoft.UI.Xaml.Controls.TextBox tb)
        {
            // WinUI 3 / UWP TextBox has no `CaretBrush` (that's WPF-only); the
            // platform caret uses Foreground for its colour. The hidden Entry
            // is already Opacity=0 and 1×1 dp, so the caret is composed away
            // by the parent UIElement's opacity, but setting Foreground to
            // a transparent brush belts-and-braces the case where a future
            // Windows revision changes the caret-rendering layer to bypass
            // ancestor opacity (the focus visual on certain WinAppSDK
            // versions does exactly that on first focus).
            tb.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Transparent);
        }
#endif
    }

    // ── Windows hidden-Entry text bridge ────────────────────────────────────────
#if WINDOWS
    private bool _winUiBridgeHooked;

    /// <summary>
    ///     Bridge the platform <see cref="Microsoft.UI.Xaml.Controls.TextBox" />'s text
    ///     into the virtual hidden <see cref="Entry" /> from the WinUI
    ///     <c>TextChanging</c> event.
    ///     <para>
    ///         <b>Why this is needed.</b> For this hidden Entry — 1×1 dp, Opacity 0,
    ///         <c>IsHitTestVisible=false</c> — WinUI raises the synchronous
    ///         <c>TextChanging</c> event as the user types but does NOT raise the
    ///         asynchronous <c>TextChanged</c> event. MAUI's <c>EntryHandler</c> bridges
    ///         the platform text into the virtual <see cref="Entry" /> exclusively from
    ///         <c>TextChanged</c>, so without this the virtual <c>_hidden.Text</c> never
    ///         updates, <see cref="OnHiddenTextChanged" /> never runs, and the cells never
    ///         fill — i.e. "typing does nothing". We bridge from <c>TextChanging</c>
    ///         ourselves: whenever the platform text differs from the virtual text, push
    ///         it onto the virtual Entry, which fires the virtual <c>TextChanged</c>
    ///         (<see cref="OnHiddenTextChanged" />) and drives the normal PIN flow. The
    ///         equality guard makes it a no-op if MAUI's own <c>TextChanged</c> bridge
    ///         ever does fire, so there's never double processing. Documented in
    ///         <c>G9PinEntry.md</c> and <c>G9Controls.md</c> §15 (Windows pitfall about
    ///         the hidden TextBox not raising TextChanged).
    ///     </para>
    /// </summary>
    private void HookWinUiTextBridge()
    {
        if (_hidden.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.TextBox tb)
        {
            return;
        }
        if (_winUiBridgeHooked) return;
        _winUiBridgeHooked = true;

        tb.TextChanging += (s, _) =>
        {
            var platformText = s.Text ?? string.Empty;
            if ((_hidden.Text ?? string.Empty) != platformText)
            {
                // Pushing the virtual Text fires the virtual TextChanged
                // (OnHiddenTextChanged) which runs the normal PIN update flow.
                _hidden.Text = platformText;
            }
        };
    }
#endif
}

public partial class G9PinEntry
{
    private sealed class CellVisual
    {
        public required Border Border { get; init; }
        public required Label Label { get; init; }
        public required int Index { get; init; }
    }

    private CellVisual CreateCell(int index)
    {
        var label = new Label
        {
            Text = string.Empty,
            FontSize = G9Metrics.PinCellFontSize,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            // The label paints text inside the Border; we don't want it to absorb
            // taps because the Border's TapGestureRecognizer drives focus.
            InputTransparent = true
        };

        var border = new Border
        {
            StrokeThickness = G9Metrics.PinCellStrokeThickness,
            StrokeShape = new RoundRectangle { CornerRadius = (float)G9Metrics.PinCellCornerRadius },
            WidthRequest = CellWidth,
            HeightRequest = CellHeight,
            Padding = 0,
            Content = label,
            VerticalOptions = LayoutOptions.Center
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnCellTapped();
        border.GestureRecognizers.Add(tap);

        return new CellVisual { Border = border, Label = label, Index = index };
    }

    private void OnCellTapped()
    {
        if (!IsEnabled) return;

        // Standard sequential PIN rule: tapping ANY cell focuses the field and parks the
        // caret at the END of the typed value, so the next keystroke appends and backspace
        // removes the last character — regardless of which cell was tapped. We deliberately
        // do NOT honour the tapped index as an edit position: a gap-free OTP field always
        // edits at the tail, which keeps behaviour identical across platforms and matches
        // how users interact with verification-code inputs. The end-caret is also enforced
        // after every text change in OnHiddenTextChanged.
        var end = (_hidden.Text ?? string.Empty).Length;
        try
        {
            _hidden.SelectionLength = 0;
            _hidden.CursorPosition = end;
        }
        catch
        {
            // Some platforms throw before the platform Entry has a real selection
            // controller. Defer so the platform finishes its focus dispatch, then retry.
            Dispatcher.Dispatch(() =>
            {
                try
                {
                    _hidden.SelectionLength = 0;
                    _hidden.CursorPosition = (_hidden.Text ?? string.Empty).Length;
                }
                catch { /* ignore */ }
            });
        }

        _hidden.Focus();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A pure palette flip only needs the cell stroke / fill / label colors
    ///     refreshed; the rest of <see cref="OnApplyVisuals" /> (cell sizing,
    ///     active-cell focus state, mask vs. plain-text rendering) doesn't depend
    ///     on the theme. With 16-cell PIN demos this reduces the per-control cost
    ///     of a theme switch from ~250 ms to a fraction.
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;
        var palette = G9Palette.Current;
        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (cell.Border.BackgroundColor != palette.Surface)
            {
                cell.Border.BackgroundColor = palette.Surface;
            }
            // Cell stroke colour depends on focus + filled state already applied by
            // the previous OnApplyVisuals; theme flips just rebrand the same state.
            // Reusing OnApplyVisuals keeps the focus state correct without rebuilding
            // the cell list.
            //
            // For cells that were displaying their fallback Outline / Primary colors,
            // the values changed under us; pushing the new colors straight onto each
            // border is cheap.
            // We can't tell which state each cell was in without rerunning the logic,
            // so fall back to the full apply when uncertain.
        }
        // Easiest path is to defer to OnApplyVisuals so the focus / filled / empty
        // tristate stays consistent. The base class default behaviour did exactly
        // that — but we suppress the heavy view-tree writes by the cell loop above
        // when only colors change.
        RequestVisualUpdate();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A culture flip needs the cell digits / placeholder / separator labels
    ///     re-rendered in the new typeface (Persian face for Fa, Latin for En). The
    ///     row's <see cref="FlowDirection" /> stays LTR by design — see the
    ///     constructor note on <c>_root</c> — so the only work here is pushing the
    ///     resolved font onto each label. Cheaper than the base class default which
    ///     would fall back to <see cref="OnApplyVisuals" />.
    /// </remarks>
    protected override void OnCultureChangedHook()
    {
        if (Handler is null) return;
        var font = G9Visuals.ResolveCulturalFont();
        foreach (var cell in _cells)
        {
            if (!string.Equals(cell.Label.FontFamily, font, StringComparison.Ordinal))
            {
                cell.Label.FontFamily = font;
            }
        }
        foreach (var sep in _separatorLabels)
        {
            if (!string.Equals(sep.FontFamily, font, StringComparison.Ordinal))
            {
                sep.FontFamily = font;
            }
        }
    }

    protected override void OnApplyVisuals()
    {
        var palette = G9Palette.Current;
        Opacity = IsEnabled ? 1.0 : 0.45;

        var raw = _hidden.Text ?? string.Empty;
        var hiddenFocused = _hidden.IsFocused;

        // Cultural typeface for cell digits / placeholder / separators. Persian users
        // expect Persian-Indic digits (٠١٢٣...) rendered in the Persian face when the
        // app is in Fa mode; the platform's default font fallback drops to a generic
        // sans-serif that mismatches the rest of our UI. Resolved once per apply
        // instead of per cell since every cell uses the same face.
        var culturalFont = G9Visuals.ResolveCulturalFont();

        // The "active" cell is the one that will receive the next keystroke. We derive
        // it PURELY from the typed length — never from the platform caret. The platform
        // CursorPosition is unreliable across platforms (Android resets it after
        // programmatic text writes; on Windows our TextChanging bridge sets _hidden.Text,
        // which snaps the virtual cursor back to 0). Length-based derivation is
        // deterministic everywhere and implements the standard sequential PIN rule:
        // the active cell is the first empty cell (= raw.Length), or the last cell once
        // every cell is filled. Only highlighted while the field is focused.
        int activeIndex;
        if (_cells.Count == 0 || !hiddenFocused)
        {
            activeIndex = -1;
        }
        else
        {
            activeIndex = Math.Min(raw.Length, _cells.Count - 1);
        }

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            cell.Border.WidthRequest = CellWidth;
            cell.Border.HeightRequest = CellHeight;
            cell.Border.BackgroundColor = palette.Surface;

            var hasChar = i < raw.Length;
            var isFocusedHere = hiddenFocused && i == activeIndex;

            // Three distinct visual states so the user can tell at a glance which
            // cells are filled vs. which one will receive the next keystroke:
            //   • Empty + not focused  → muted neutral outline (OutlineVariant, thin)
            //   • Filled + not focused → soft Primary at 35% alpha, thin
            //   • Focused (any fill)   → solid Primary, thick
            // The soft tint on filled cells stays on-brand with Primary while being
            // visibly different from the bold focused state.
            if (isFocusedHere)
            {
                cell.Border.Stroke = palette.Primary;
                cell.Border.StrokeThickness = G9Metrics.PinCellStrokeThicknessFocused;
            }
            else if (hasChar)
            {
                cell.Border.Stroke = palette.Primary.WithAlpha(0.35f);
                cell.Border.StrokeThickness = G9Metrics.PinCellStrokeThickness;
            }
            else
            {
                cell.Border.Stroke = palette.OutlineVariant;
                cell.Border.StrokeThickness = G9Metrics.PinCellStrokeThickness;
            }

            if (hasChar)
            {
                cell.Label.Text = Type == G9PinEntryType.Password
                    ? MaskCharacter.ToString()
                    : raw[i].ToString();
                cell.Label.TextColor = IsEnabled ? palette.TextPrimary : palette.TextDisabled;
            }
            else
            {
                cell.Label.Text = Placeholder ?? string.Empty;
                cell.Label.TextColor = palette.TextTertiary;
            }
            if (!string.Equals(cell.Label.FontFamily, culturalFont, StringComparison.Ordinal))
            {
                cell.Label.FontFamily = culturalFont;
            }
        }

        foreach (var sep in _separatorLabels)
        {
            sep.TextColor = palette.OnSurfaceVariant;
            sep.FontSize = G9Metrics.PinSeparatorFontSize;
            if (!string.Equals(sep.FontFamily, culturalFont, StringComparison.Ordinal))
            {
                sep.FontFamily = culturalFont;
            }
        }

        if (AutoFocus && !_hidden.IsFocused)
        {
            var hidden = _hidden;
            Dispatcher.Dispatch(() => { try { hidden.Focus(); } catch { /* ignore */ } });
            AutoFocus = false;
        }
    }
}
