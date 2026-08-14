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

        stack.Add(ActionsPage.Section("G9Editor", palette,
            new G9Editor { Label = "Multi-line", Text = "First line\nSecond line", AutoSize = EditorAutoSizeOption.TextChanges },
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

    private static View NewPicker(string label) => new G9Picker
    {
        Label = label,
        ItemsSource = BuildItems()
    };

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
