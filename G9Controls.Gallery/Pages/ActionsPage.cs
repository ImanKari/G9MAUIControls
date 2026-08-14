using G9MAUIControls.Controls;
using G9MAUIControls.Hosting;
using G9MAUIControls.Icons;
using G9MAUIControls.Theming;

namespace G9Controls.Gallery.Pages;

/// <summary>
///     Buttons, cards and feedback controls, across every variant.
///     <para>
///         The variant grid is the real test. Each of the ten variants maps onto specific palette tokens, and
///         the failure mode is subtle: a variant pointed at <c>Primary</c> instead of its own semantic token
///         makes every button read as "all good" regardless of what it does. Confirm Error is red, Warning is
///         amber, and no two variants look alike.
///     </para>
///     <para>
///         Also verify press feedback fires at the <b>edges</b> of each control, not just the middle. A
///         too-small hit target only fails at its edges, which is why such bugs get reported as
///         "intermittent" (see <c>G9Controls.md</c> §10b).
///     </para>
/// </summary>
public sealed class ActionsPage : G9PageBase
{
    public ActionsPage()
    {
        Title = "Actions";
        Content = Build();
    }

    private static View Build()
    {
        var palette = G9Palette.Current;
        var stack = new VerticalStackLayout { Spacing = 18, Padding = new Thickness(16) };

        stack.Add(new Label
        {
            Text = "No two variants should look alike, and Error/Warning must not read as Primary. "
                 + "Press each control at its EDGE — a missed edge press is a hit-target bug.",
            FontSize = 12,
            TextColor = palette.OnSurfaceVariant
        });

        var variants = new VerticalStackLayout { Spacing = 10 };
        foreach (var variant in Enum.GetValues<G9ButtonVariant>())
        {
            variants.Add(new G9Button
            {
                Text = variant.ToString(),
                Variant = variant,
                LeadingIcon = G9Glyph.Check
            });
        }

        variants.Add(new G9Button { Text = "Loading", Variant = G9ButtonVariant.Primary, IsLoading = true });
        variants.Add(new G9Button { Text = "Disabled", Variant = G9ButtonVariant.Primary, IsEnabled = false });

        stack.Add(Section("G9Button — every variant", palette, variants));

        stack.Add(Section("G9IconButton", palette, new HorizontalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new G9IconButton { Icon = G9Glyph.Search },
                new G9IconButton { Icon = G9Glyph.Delete },
                new G9IconButton { Icon = G9Glyph.Refresh },
                new G9IconButton { Icon = G9Glyph.Close, IsEnabled = false }
            }
        }));

        // Value is a 0..1 ratio, not a percentage — a control fed 35 instead of 0.35 pins itself full and
        // looks "finished", which is why every type appears here at a distinct fraction.
        stack.Add(Section("G9ProgressBar", palette,
            new G9ProgressBar { Value = 0.35 },
            new G9ProgressBar { Value = 0.7, ProgressType = G9ProgressType.Success },
            new G9ProgressBar { Value = 0.5, ProgressType = G9ProgressType.Warning, ShowSegments = true },
            new G9ProgressBar { Value = 0.2, ProgressType = G9ProgressType.Error, IsPaused = true },
            new G9ProgressBar { IsIndeterminate = true }));

        stack.Add(Section("G9Separator", palette,
            new G9Separator { Title = "A titled separator", Icon = G9Glyph.Info },
            new G9Separator { Title = "End-aligned", TitleAlignment = G9SeparatorTitleAlignment.End },
            new G9Separator()));

        return new ScrollView { Content = stack };
    }

    internal static View Section(string title, G9Palette palette, params View[] children)
    {
        var body = new VerticalStackLayout { Spacing = 12 };
        foreach (var child in children)
        {
            body.Add(child);
        }

        return new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = palette.Primary
                },
                new Border
                {
                    BackgroundColor = palette.CardBackground,
                    Stroke = palette.OutlineBorder,
                    StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
                    Padding = new Thickness(14),
                    Content = body
                }
            }
        };
    }
}
