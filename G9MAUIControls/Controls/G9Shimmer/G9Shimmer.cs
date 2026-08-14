using G9MAUIControls.BottomSheet;
using G9MAUIControls.Theming;
using Microsoft.Maui.Controls.Shapes;

namespace G9MAUIControls.Controls;

/// <summary>
///     Builds shimmer skeleton placeholders: static gray shape rows (cheap plain MAUI views —
///     zero animation cost) overlaid by a <see cref="G9ShimmerBandView" /> highlight sweep whose
///     animation runs on the platform render/compositor thread, so it stays fluid while the UI
///     thread builds the heavy content the skeleton is masking. Consumed by
///     <c>G9BottomSheetHelper</c> through <c>G9BottomSheetOptions.LoadingSkeleton</c> as the
///     <c>DeferredContentView.LoadingView</c>; usable anywhere else a loading placeholder view is
///     accepted. See <c>G9Shimmer.md</c>.
/// </summary>
public static class G9Shimmer
{
    private const int DefaultListRows = 5;
    private const int DefaultFormRows = 4;
    private const int MinRows = 2;
    private const int MaxRows = 10;

    /// <summary>
    ///     Creates the skeleton placeholder for a deferred bottom-sheet body. The returned view
    ///     paints an opaque background (like the default spinner placeholder) so underlying
    ///     content never leaks through while loading.
    /// </summary>
    public static View CreateSheetSkeleton(
        G9BottomSheetLoadingSkeleton skeleton,
        int rowCount,
        Color? backgroundColor)
    {
        var host = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsClippedToBounds = true,
            BackgroundColor = backgroundColor ?? G9Palette.Current.Background,
            InputTransparent = true
        };

        var rows = skeleton == G9BottomSheetLoadingSkeleton.FormFields
            ? BuildFormFieldRows(ResolveRowCount(rowCount, DefaultFormRows))
            : BuildListRows(ResolveRowCount(rowCount, DefaultListRows));
        host.Children.Add(rows);

#if ANDROID || IOS || MACCATALYST
        // The render-thread sweep. Windows keeps the static skeleton only (no handler there) —
        // a UI-thread band would freeze exactly when the skeleton is needed, which is the
        // failure mode this control exists to avoid.
        host.Children.Add(new G9ShimmerBandView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        });
#endif

        return host;
    }

    private static int ResolveRowCount(int requested, int fallback)
    {
        return Math.Clamp(requested > 0 ? requested : fallback, MinRows, MaxRows);
    }

    private static View BuildListRows(int rowCount)
    {
        var stack = new VerticalStackLayout
        {
            Padding = G9LayoutMetrics.ModalBaseBodyMargin,
            Spacing = G9LayoutMetrics.SpacingMedium,
            VerticalOptions = LayoutOptions.Start
        };

        for (var i = 0; i < rowCount; i++)
        {
            var row = new Grid
            {
                ColumnSpacing = G9LayoutMetrics.SpacingMedium,
                HeightRequest = G9Metrics.SelectionRowHeight,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Star }
                }
            };

            var circle = CreateShape(28, 28, 14);
            circle.VerticalOptions = LayoutOptions.Center;
            row.Children.Add(circle);

            var bar = CreateShape(0, 16, 8);
            bar.HorizontalOptions = LayoutOptions.Fill;
            bar.VerticalOptions = LayoutOptions.Center;
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);

            stack.Children.Add(row);
        }

        return stack;
    }

    private static View BuildFormFieldRows(int rowCount)
    {
        var stack = new VerticalStackLayout
        {
            Padding = G9LayoutMetrics.ModalBaseBodyMargin,
            Spacing = G9LayoutMetrics.SpacingLarge,
            VerticalOptions = LayoutOptions.Start
        };

        for (var i = 0; i < rowCount; i++)
        {
            var field = CreateShape(0, 48, 12);
            field.HorizontalOptions = LayoutOptions.Fill;
            stack.Children.Add(field);
        }

        return stack;
    }

    private static Border CreateShape(double width, double height, double cornerRadius)
    {
        var shape = new Border
        {
            StrokeThickness = 0,
            HeightRequest = height,
            BackgroundColor = G9Palette.Current.SurfaceContainerHigh,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(cornerRadius) }
        };

        if (width > 0)
        {
            shape.WidthRequest = width;
        }

        return shape;
    }
}
