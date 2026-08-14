namespace G9MAUIControls.BottomSheet;

/// <summary>
///     MAUI builder hook for the <see cref="G9SheetView" /> control. Registers the custom
///     <see cref="G9SheetViewBorder" /> handler on every supported platform — Android,
///     iOS, Mac Catalyst, and Windows. Call once during app startup before
///     <c>builder.Build()</c>.
/// </summary>
public static class G9SheetViewMauiAppBuilderExtensions
{
    /// <summary>
    ///     Registers the <see cref="G9SheetViewBorder" /> handler with MAUI so the bottom
    ///     sheet body can intercept platform-specific touch events for state drag and
    ///     inner-scrollable handoff.
    /// </summary>
    public static MauiAppBuilder UseG9SheetView(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID || IOS || MACCATALYST || WINDOWS
            handlers.AddHandler<G9SheetViewBorder, G9SheetViewBorderHandler>();
#endif
        });

        // Hide scroll bars on every scroller inside a sheet (scrolling stays functional).
        G9BottomSheetScrollBarPolicy.Register();

        return builder;
    }
}
