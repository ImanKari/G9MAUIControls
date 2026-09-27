using System.Collections.ObjectModel;
using G9MAUIControls.Controls;
using G9MAUIControls.Hosting;
using G9MAUIControls.Icons;
using G9MAUIControls.Theming;

namespace G9Controls.Gallery.Pages;

/// <summary>
///     Every input control, in the states that actually break.
///     <para>
///         Each control appears empty, filled, with a leading icon, with an error, and disabled — because
///         those transitions are where the outlined-field architecture has historically failed. Two in
///         particular are worth staring at:
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Disabled, on Android.</b> The floated label sits half outside the box by Material
///             convention, and an <c>Opacity &lt; 1</c> on the field forces an offscreen alpha layer clipped
///             to the view's own bounds — which cuts the label in half. The base dims children individually
///             to avoid it (§15 A5). This page is where a regression shows.
///         </item>
///         <item>
///             <b>RTL.</b> Icon slots must physically swap columns, and the floated label must anchor to the
///             other edge. A tick or chevron that mirrors is a bug; the box mirroring is correct.
///         </item>
///     </list>
/// </summary>
public sealed class InputsPage : G9PageBase
{
    public InputsPage()
    {
        Title = "Inputs";
        Content = Build();
    }

    private static View Build()
    {
        var palette = G9Palette.Current;
        var stack = new VerticalStackLayout { Spacing = 18, Padding = new Thickness(16) };

        stack.Add(new Label
        {
            Text = "Check each control empty / filled / with icon / error / disabled. On Android look hard at "
                 + "the DISABLED rows: a clipped floating label is the §15 A5 regression.",
            FontSize = 12,
            TextColor = palette.OnSurfaceVariant
        });

        stack.Add(ActionsPage.Section("G9TextEntry", palette,
            new G9TextEntry { Label = "Empty" },
            new G9TextEntry { Label = "Filled", Text = "A value" },
            new G9TextEntry { Label = "With leading icon", LeadingIcon = G9Glyph.Search, Text = "Searchable" },
            new G9TextEntry { Label = "Password", IsPassword = true, PasswordToggle = true, Text = "secret" },
            new G9TextEntry { Label = "Error", Text = "bad", HasError = true, ErrorText = "Invalid value" },
            new G9TextEntry { Label = "Helper", Text = "ok", HelperText = "A hint below the field" },
            new G9TextEntry { Label = "Counter", Text = "count me", MaxLength = 20, ShowCharacterCounter = true },
            new G9TextEntry { Label = "Disabled", Text = "Not editable", IsEnabled = false }));

        // FilledValueHighlight (ADR-0025). The same filled, unfocused fields in both styles, side by side.
        // Accent: outline, floated label and trailing icon in Primary — the library default. Neutral: the
        // exact greys of the EMPTY field above, with the label still floated and bold. Focus either one:
        // both must turn Primary; set an error: both must turn red. A picker is included because a
        // pre-selected picker is the case that reads as "focused" when it is not.
        stack.Add(ActionsPage.Section("Filled value: accent vs neutral", palette,
            new G9TextEntry { Label = "Accent (explicit)", Text = "A value", FilledValueHighlight = G9FilledValueHighlight.Accent },
            new G9TextEntry { Label = "Neutral (explicit)", Text = "A value", FilledValueHighlight = G9FilledValueHighlight.Neutral },
            NewPicker("Picker, pre-selected — accent", G9FilledValueHighlight.Accent, preselect: true),
            NewPicker("Picker, pre-selected — neutral", G9FilledValueHighlight.Neutral, preselect: true),
            new G9TextEntry
            {
                Label = "Neutral + error",
                Text = "bad",
                HasError = true,
                ErrorText = "Error still wins",
                FilledValueHighlight = G9FilledValueHighlight.Neutral
            }));

        // ITCS-15685: an editor's trailing icon (the dictation mic, or any TrailingIcon) belongs in the
        // BOTTOM-END corner of the box. The second field is the configuration that used to centre it
        // vertically — a MaxEditorHeight ceiling turned the box's measure into an AT_MOST constraint the
        // ripple layer grew into (LES-0050). The gallery registers no speech provider, so a plain trailing
        // icon stands in for the microphone: same slot, same host. Both must show the glyph 8dp above the
        // bottom edge, empty AND after typing a few lines, in both directions (switch the culture).
        stack.Add(ActionsPage.Section("G9Editor", palette,
            new G9Editor { Label = "Multi-line", Text = "First line\nSecond line", AutoSize = EditorAutoSizeOption.TextChanges },
            new G9Editor
            {
                Label = "AlwaysFloat + Min 90 / Max 210 + trailing icon (the observation form)",
                AlwaysFloat = true,
                MinimumEditorHeight = 90,
                MaxEditorHeight = 210,
                MaxLength = 1000,
                TrailingIcon = G9Glyph.Info
            },
            new G9Editor
            {
                Label = "Min 110 + counter + trailing icon (the tester form)",
                MinimumEditorHeight = 110,
                MaxLength = 4000,
                ShowCharacterCounter = true,
                TrailingIcon = G9Glyph.Info
            },
            new G9Editor { Label = "Disabled", Text = "Locked", IsEnabled = false }));

        // The mic is hidden unless a speech provider is registered. The gallery registers none on purpose,
        // so the mic's ABSENCE here is the expected result — it proves the core has no speech dependency.
        stack.Add(ActionsPage.Section("G9SearchEntry", palette,
            new G9SearchEntry { Label = "Search (no mic: no IG9SpeechToText registered)", VoiceEnabled = true },
            new G9SearchEntry { Label = "Debounced", DebounceMs = 400 }));

        stack.Add(ActionsPage.Section("G9ComboBox / G9Picker", palette,
            NewCombo("Single select", multiple: false),
            NewCombo("Multi select", multiple: true),
            NewPicker("Picker (single, sheet-based)")));

        stack.Add(ActionsPage.Section("G9DateTimePicker", palette,
            new G9DateTimePicker { Label = "Date", Mode = G9DateTimePickerMode.Date },
            new G9DateTimePicker { Label = "Time", Mode = G9DateTimePickerMode.Time },
            new G9DateTimePicker { Label = "Date + time", Mode = G9DateTimePickerMode.DateTime, ShowTodayButton = true }));

        stack.Add(ActionsPage.Section("G9PinEntry", palette,
            new G9PinEntry { Length = 5 },
            new G9PinEntry { Length = 6, Type = G9PinEntryType.Number, GroupSizes = "3,3", Separator = "-" }));

        // A checked glyph inside a mirrored canvas is the §9 double-flip bug: the switch pins its
        // GraphicsView to LTR so the tick cannot come out backwards in RTL.
        stack.Add(ActionsPage.Section("G9Switch", palette,
            new G9Switch { Title = "Off", IsInFormRow = true },
            new G9Switch { Title = "On", Description = "With a description line", IsOn = true, IsInFormRow = true },
            new G9Switch { Title = "Disabled, on", IsOn = true, IsEnabled = false, IsInFormRow = true }));

        // G9CheckBox (ADR-0028). What to look at: the tick DRAWS ITSELF IN on check and un-draws on
        // uncheck (tap twice quickly — it must turn around mid-way, not restart); the halo under the
        // finger; tapping the LABEL toggles too; a tap on an indeterminate box CHECKS it; the disabled
        // rows ignore taps; in the RTL rows the box is on the right and the tick is NOT mirrored; the
        // wrapped label keeps the box level with its first line. Press the EDGES of each row (§10b).
        stack.Add(ActionsPage.Section("G9CheckBox", palette,
            new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    new G9CheckBox { Text = "Off" },
                    new G9CheckBox { Text = "On", IsChecked = true },
                    new G9CheckBox { Text = "Indeterminate — a tap checks it", IsIndeterminate = true },
                    new G9CheckBox { Text = "Disabled, off", IsEnabled = false },
                    new G9CheckBox { Text = "Disabled, on", IsChecked = true, IsEnabled = false },
                    new G9CheckBox { Text = "Disabled, indeterminate", IsIndeterminate = true, IsEnabled = false }
                }
            },
            new HorizontalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    new G9CheckBox(),
                    new G9CheckBox { IsChecked = true },
                    new G9CheckBox { IsIndeterminate = true },
                    new G9CheckBox { IsChecked = true, IsEnabled = false }
                }
            },
            NewSelectAllDemo(),
            new VerticalStackLayout
            {
                Spacing = 0,
                FlowDirection = FlowDirection.RightToLeft,
                Children =
                {
                    new G9CheckBox { Text = "RTL: the box is on the right, the tick is not mirrored", IsChecked = true },
                    new G9CheckBox
                    {
                        Text = "RTL with a long label that wraps onto a second line, so the box has to stay "
                             + "level with the FIRST line instead of centring on the paragraph"
                    }
                }
            }));

        stack.Add(ActionsPage.Section("G9RangeSlider", palette,
            new G9RangeSlider { Minimum = 0, Maximum = 100, Value = 40, Mode = G9RangeSliderMode.Single, ShowLabels = true },
            new G9RangeSlider
            {
                Minimum = 0, Maximum = 100, RangeStart = 20, RangeEnd = 80,
                Mode = G9RangeSliderMode.Range, ShowLabels = true
            }));

        stack.Add(ActionsPage.Section("G9ChipGroup", palette,
            NewChips(G9ChipGroupSelectionMode.SingleSelection),
            NewChips(G9ChipGroupSelectionMode.MultiSelection)));

        return new ScrollView { Content = stack };
    }

    private static View NewCombo(string label, bool multiple) => new G9ComboBox
    {
        Label = label,
        AllowMultipleSelection = multiple,
        ClearButton = true,
        ItemsSource = BuildItems()
    };

    private static View NewPicker(
        string label,
        G9FilledValueHighlight highlight = G9FilledValueHighlight.Inherit,
        bool preselect = false)
    {
        var items = BuildItems();
        return new G9Picker
        {
            Label = label,
            ItemsSource = items,
            SelectedItem = preselect ? items[0] : null,
            FilledValueHighlight = highlight
        };
    }

    /// <summary>
    ///     The tri-state pattern the indeterminate state exists for: a parent box that is ticked when
    ///     every child is, empty when none is, and indeterminate in between. Tapping the parent while it
    ///     is indeterminate CHECKS it (and so every child) — the platform convention.
    /// </summary>
    private static View NewSelectAllDemo()
    {
        G9CheckBox[] children =
        [
            new() { Text = "Soil", IsChecked = true },
            new() { Text = "Water" },
            new() { Text = "Weather" }
        ];
        var parent = new G9CheckBox { Text = "Select all (tri-state)" };
        var syncing = false;

        void SyncParentFromChildren()
        {
            if (syncing) return;
            syncing = true;
            var checkedCount = children.Count(static c => c.IsChecked);
            parent.IsIndeterminate = checkedCount > 0 && checkedCount < children.Length;
            parent.IsChecked = checkedCount == children.Length;
            syncing = false;
        }

        parent.CheckedChanged += (_, e) =>
        {
            if (syncing) return;
            syncing = true;
            foreach (var child in children)
            {
                child.IsChecked = e.Value;
            }

            syncing = false;
        };

        var layout = new VerticalStackLayout { Spacing = 0 };
        layout.Add(parent);
        foreach (var child in children)
        {
            // Indent the children under the parent's label. Physical-left is fine here: this demo
            // row is LTR (the RTL rows are the ones after it).
            child.Margin = new Thickness(28, 0, 0, 0);
            child.CheckedChanged += (_, _) => SyncParentFromChildren();
            layout.Add(child);
        }

        SyncParentFromChildren();
        return layout;
    }

    private static View NewChips(G9ChipGroupSelectionMode mode) => new G9ChipGroup
    {
        SelectionMode = mode,
        ShowSelectionCheckmark = true,
        ItemsSource = BuildItems()
    };

    /// <summary>
    ///     Items are <see cref="ObservableCollection{T}" /> because the controls observe them: a
    ///     <c>List&lt;T&gt;</c> would bind but never react to a later Add.
    /// </summary>
    private static ObservableCollection<G9SelectionItem> BuildItems() =>
    [
        new() { Text = "Alpha", Key = "a", Icon = G9Glyph.Check },
        new() { Text = "Beta", Key = "b", Icon = G9Glyph.Info },
        new() { Text = "Gamma — a deliberately long label to exercise truncation", Key = "c" },
        new() { Text = "Delta (disabled)", Key = "d", IsEnabled = false }
    ];
}
