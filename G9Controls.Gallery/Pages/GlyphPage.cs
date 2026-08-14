using G9MAUIControls.Hosting;
using G9MAUIControls.Icons;
using G9MAUIControls.Theming;

namespace G9Controls.Gallery.Pages;

/// <summary>
///     Every built-in vector glyph, at four sizes, on both a surface and an accent fill.
///     <para>
///         <b>This page is the whole reason the gallery exists first.</b> The ~15 glyphs in
///         <see cref="G9GlyphDrawable" /> are hand-authored <c>PathF</c> geometry on a 24×24 grid and had
///         never been rendered when they were written. A path that is subtly off-centre, or whose stroke
///         weight drifts from its neighbours, compiles perfectly and is only findable by eye.
///     </para>
///     <para>
///         <b>Four sizes, not one.</b> The grid scales to the requested box, so an error in the design-box
///         coordinates shows up as a glyph that looks right at 24 and wrong at 14 or 44 — which is exactly
///         the range the controls use (helper rows at 14, fields at 20, buttons at 24, FABs at 42).
///     </para>
///     <para>
///         <b>Both backgrounds, because contrast hides different faults.</b> A stroke that is too thin
///         disappears on an accent fill while looking acceptable on a surface; an off-centre glyph is
///         obvious inside a circular fill and invisible on a flat one.
///     </para>
/// </summary>
public sealed class GlyphPage : G9PageBase
{
    private static readonly double[] Sizes = [14, 20, 24, 44];

    public GlyphPage()
    {
        Title = "Glyphs";
        Content = Build();
    }

    private static View Build()
    {
        var palette = G9Palette.Current;

        var glyphs = Enum.GetValues<G9Glyph>().Where(g => g != G9Glyph.None).ToArray();

        var rows = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(16) };

        rows.Add(new Label
        {
            Text = $"{glyphs.Length} built-in vector glyphs · no font · sizes {string.Join(" / ", Sizes)}",
            FontSize = 13,
            TextColor = palette.OnSurfaceVariant
        });

        rows.Add(new Label
        {
            Text = "Look for: off-centre geometry, stroke weight drifting between neighbours, a glyph that "
                 + "reads correctly at 24 but not at 14, and anything that loses legibility on the accent fill.",
            FontSize = 12,
            TextColor = palette.OnSurfaceVariant
        });

        foreach (var glyph in glyphs)
        {
            rows.Add(BuildGlyphRow(glyph, palette));
        }

        return new ScrollView { Content = rows };
    }

    private static View BuildGlyphRow(G9Glyph glyph, G9Palette palette)
    {
        var onSurface = new HorizontalStackLayout { Spacing = 14, VerticalOptions = LayoutOptions.Center };
        foreach (var size in Sizes)
        {
            onSurface.Add(new G9IconView { Icon = glyph, Color = palette.OnSurface, Size = size });
        }

        // The same glyphs on the accent fill. A stroke that is too light vanishes here first.
        var onAccent = new HorizontalStackLayout { Spacing = 14, VerticalOptions = LayoutOptions.Center };
        foreach (var size in Sizes)
        {
            onAccent.Add(new G9IconView { Icon = glyph, Color = palette.OnPrimary, Size = size });
        }

        return new Border
        {
            BackgroundColor = palette.CardBackground,
            Stroke = palette.OutlineBorder,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(12),
            Content = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(new GridLength(120)), new ColumnDefinition(GridLength.Star)],
                RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
                RowSpacing = 10,
                ColumnSpacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = glyph.ToString(),
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = palette.OnSurface,
                        VerticalOptions = LayoutOptions.Center
                    },
                    WithGrid(onSurface, row: 0, column: 1),
                    WithGrid(new Label
                    {
                        Text = "on accent",
                        FontSize = 11,
                        TextColor = palette.OnSurfaceVariant,
                        VerticalOptions = LayoutOptions.Center
                    }, row: 1, column: 0),
                    WithGrid(new Border
                    {
                        BackgroundColor = palette.Primary,
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
                        Padding = new Thickness(10, 8),
                        HorizontalOptions = LayoutOptions.Start,
                        Content = onAccent
                    }, row: 1, column: 1)
                }
            }
        };
    }

    private static View WithGrid(View view, int row, int column)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        return view;
    }
}
