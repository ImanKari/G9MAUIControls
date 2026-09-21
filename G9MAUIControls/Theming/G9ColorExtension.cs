using System.ComponentModel;
using Microsoft.Maui.Controls.Xaml;

namespace G9MAUIControls.Theming;

/// <summary>
///     XAML markup extension that pushes a colour from <see cref="G9Palette.Current" />
///     onto the target's <see cref="BindableProperty" /> and keeps it in sync when the
///     palette changes. Equivalent in spirit to <c>{Binding Primary, Source={x:Static
///     G9Palette.Current}}</c> but bypasses MAUI's binding pipeline by subscribing
///     directly to <see cref="G9Palette.Current" />'s <c>PropertyChanged</c> and
///     writing the new value via <see cref="BindableObject.SetValue(BindableProperty, object)" />.
///     <para>
///         <b>Why bypass the binding system?</b> A theme switch fires
///         <see cref="System.ComponentModel.PropertyChangedEventArgs" /> with
///         <see cref="string.Empty" /> as the property name (the INPC convention for
///         "every property is invalidated"). MAUI's binding pipeline reacts by
///         re-resolving every <see cref="Binding" /> listening on the source — for the
///         dense Controls showcase this is hundreds of bindings, and the per-binding
///         resolution cost on Android (reflection + JNI marshalling) measured at
///         ~4 seconds wall-clock just for the binding fan-out alone. A direct
///         <see cref="BindableObject.SetValue(BindableProperty, object)" /> push from
///         a single <see cref="G9Palette" /> subscriber is dramatically cheaper.
///     </para>
///     <para>
///         <b>Lifetime</b>. The subscription is held by a weak reference to the
///         target so we don't pin the visual element in memory after the page is
///         disposed. Dead entries are dropped on each fan-out and — because a fan-out only
///         happens on a theme switch — also by an amortised sweep as new subscriptions
///         register (see <see cref="G9PaletteSubscriptions.Register" />).
///     </para>
///     <para>
///         <b>Backwards compatibility</b>. Consumers continue to write
///         <c>{themeManager:ThemeColor Primary}</c> and <c>{themeManager:ThemeColor
///         Primary, Alpha=0.35}</c>; the extension surface is unchanged.
///     </para>
/// </summary>
[ContentProperty(nameof(Key))]
[RequireService([typeof(IProvideValueTarget)])]
public sealed class G9ColorExtension : IMarkupExtension
{
    public G9ColorExtension() { }

    public G9ColorExtension(G9ColorToken key)
    {
        Key = key;
    }

    public G9ColorToken Key { get; set; }
    public double? Alpha { get; set; }

    public object ProvideValue(IServiceProvider serviceProvider)
    {
        var initial = ResolveColor(Key, Alpha);

        // Pull target via IProvideValueTarget. When the markup extension is used in a
        // place where MAUI doesn't surface a target (style setters, attached
        // properties via a path, certain in-line conversions), fall back to returning
        // the snapshot colour and skipping the live subscription. Static look-ups
        // happen on the rare slow path (theme-change won't repaint these) but the
        // common visual-tree usage gets the live wiring.
        if (serviceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt
            && pvt.TargetObject is BindableObject targetObject
            && pvt.TargetProperty is BindableProperty bp)
        {
            G9PaletteSubscriptions.Register(targetObject, bp, Key, Alpha);

            // For Brush-typed properties (e.g. Border.Stroke) wrap the snapshot in a
            // SolidColorBrush so the initial XAML attribute parse hits the right
            // type. Subsequent updates go through ConvertForProperty in the
            // subscription side.
            if (bp.ReturnType == typeof(Brush) || typeof(Brush).IsAssignableFrom(bp.ReturnType))
            {
                return new SolidColorBrush(initial);
            }
        }

        return initial;
    }

    /// <summary>
    ///     Resolve the target colour for the supplied key with optional alpha
    ///     blending. Centralised so the live subscriber and the static initial fetch
    ///     share the same code path.
    /// </summary>
    public static Color ResolveColor(G9ColorToken key, double? alpha)
    {
        var color = ReadPaletteColor(key);
        if (alpha is null) return color;
        return color.WithAlpha((float)Math.Clamp(alpha.Value, 0d, 1d));
    }

    /// <summary>
    ///     Token → palette property, as a compiled <c>switch</c> over the (dense) enum.
    ///     <para>
    ///         This was <c>typeof(G9Palette).GetProperty(key.ToString())</c> behind a static
    ///         <c>Dictionary</c> cache, then <c>PropertyInfo.GetValue</c> on every read — and a theme
    ///         switch reads once per live <c>{G9Color}</c> usage, well over a thousand on a dense app. The
    ///         switch costs a jump table, allocates nothing, needs no cache (so nothing to synchronise
    ///         when a view is inflated off the main thread), and is visible to the trimmer: the library
    ///         is <c>IsAotCompatible</c>, and a by-name property lookup is exactly what trimming breaks.
    ///     </para>
    ///     Every <see cref="G9ColorToken" /> has an arm; a token added without one falls back to the
    ///     same loud magenta the reflection path returned for a missing property.
    /// </summary>
    private static Color ReadPaletteColor(G9ColorToken key)
    {
        var palette = G9Palette.Current;

        Color? color = key switch
        {
            G9ColorToken.Primary => palette.Primary,
            G9ColorToken.OnPrimary => palette.OnPrimary,
            G9ColorToken.PrimaryContainer => palette.PrimaryContainer,
            G9ColorToken.OnPrimaryContainer => palette.OnPrimaryContainer,
            G9ColorToken.PrimaryBorder => palette.PrimaryBorder,
            G9ColorToken.PrimaryHover => palette.PrimaryHover,
            G9ColorToken.PrimaryPressed => palette.PrimaryPressed,
            G9ColorToken.Secondary => palette.Secondary,
            G9ColorToken.OnSecondary => palette.OnSecondary,
            G9ColorToken.SecondaryContainer => palette.SecondaryContainer,
            G9ColorToken.OnSecondaryContainer => palette.OnSecondaryContainer,
            G9ColorToken.SecondaryBorder => palette.SecondaryBorder,
            G9ColorToken.Tertiary => palette.Tertiary,
            G9ColorToken.OnTertiary => palette.OnTertiary,
            G9ColorToken.TertiaryContainer => palette.TertiaryContainer,
            G9ColorToken.OnTertiaryContainer => palette.OnTertiaryContainer,
            G9ColorToken.TertiaryBorder => palette.TertiaryBorder,
            G9ColorToken.Quaternary => palette.Quaternary,
            G9ColorToken.OnQuaternary => palette.OnQuaternary,
            G9ColorToken.QuaternaryContainer => palette.QuaternaryContainer,
            G9ColorToken.OnQuaternaryContainer => palette.OnQuaternaryContainer,
            G9ColorToken.QuaternaryBorder => palette.QuaternaryBorder,
            G9ColorToken.Error => palette.Error,
            G9ColorToken.OnError => palette.OnError,
            G9ColorToken.ErrorContainer => palette.ErrorContainer,
            G9ColorToken.OnErrorContainer => palette.OnErrorContainer,
            G9ColorToken.ErrorBorder => palette.ErrorBorder,
            G9ColorToken.Warning => palette.Warning,
            G9ColorToken.OnWarning => palette.OnWarning,
            G9ColorToken.WarningContainer => palette.WarningContainer,
            G9ColorToken.OnWarningContainer => palette.OnWarningContainer,
            G9ColorToken.WarningBorder => palette.WarningBorder,
            G9ColorToken.Success => palette.Success,
            G9ColorToken.OnSuccess => palette.OnSuccess,
            G9ColorToken.SuccessContainer => palette.SuccessContainer,
            G9ColorToken.OnSuccessContainer => palette.OnSuccessContainer,
            G9ColorToken.SuccessBorder => palette.SuccessBorder,
            G9ColorToken.Info => palette.Info,
            G9ColorToken.OnInfo => palette.OnInfo,
            G9ColorToken.InfoContainer => palette.InfoContainer,
            G9ColorToken.OnInfoContainer => palette.OnInfoContainer,
            G9ColorToken.InfoBorder => palette.InfoBorder,
            G9ColorToken.Outline => palette.Outline,
            G9ColorToken.OutlineVariant => palette.OutlineVariant,
            G9ColorToken.OutlineBorder => palette.OutlineBorder,
            G9ColorToken.Background => palette.Background,
            G9ColorToken.OnBackground => palette.OnBackground,
            G9ColorToken.BackgroundSecondary => palette.BackgroundSecondary,
            G9ColorToken.Surface => palette.Surface,
            G9ColorToken.OnSurface => palette.OnSurface,
            G9ColorToken.SurfaceVariant => palette.SurfaceVariant,
            G9ColorToken.OnSurfaceVariant => palette.OnSurfaceVariant,
            G9ColorToken.SurfaceBorder => palette.SurfaceBorder,
            G9ColorToken.InverseSurface => palette.InverseSurface,
            G9ColorToken.InverseOnSurface => palette.InverseOnSurface,
            G9ColorToken.InversePrimary => palette.InversePrimary,
            G9ColorToken.SurfaceContainerHighest => palette.SurfaceContainerHighest,
            G9ColorToken.SurfaceContainerHigh => palette.SurfaceContainerHigh,
            G9ColorToken.SurfaceContainer => palette.SurfaceContainer,
            G9ColorToken.SurfaceContainerLow => palette.SurfaceContainerLow,
            G9ColorToken.SurfaceContainerLowest => palette.SurfaceContainerLowest,
            G9ColorToken.SurfaceBright => palette.SurfaceBright,
            G9ColorToken.SurfaceDim => palette.SurfaceDim,
            G9ColorToken.Scrim => palette.Scrim,
            G9ColorToken.ScrimLight => palette.ScrimLight,
            G9ColorToken.Overlay => palette.Overlay,
            G9ColorToken.OverlayLight => palette.OverlayLight,
            G9ColorToken.TextPrimary => palette.TextPrimary,
            G9ColorToken.TextSecondary => palette.TextSecondary,
            G9ColorToken.TextTertiary => palette.TextTertiary,
            G9ColorToken.TextDisabled => palette.TextDisabled,
            G9ColorToken.White => palette.White,
            G9ColorToken.Black => palette.Black,
            G9ColorToken.Light => palette.Light,
            G9ColorToken.Dark => palette.Dark,
            G9ColorToken.Divider => palette.Divider,
            G9ColorToken.DividerStrong => palette.DividerStrong,
            G9ColorToken.Focus => palette.Focus,
            G9ColorToken.FocusVisible => palette.FocusVisible,
            G9ColorToken.Disabled => palette.Disabled,
            G9ColorToken.DisabledContainer => palette.DisabledContainer,
            G9ColorToken.CardBackground => palette.CardBackground,
            G9ColorToken.CardBorder => palette.CardBorder,
            G9ColorToken.CardElevated => palette.CardElevated,
            G9ColorToken.Default => palette.Default,
            G9ColorToken.OnDefault => palette.OnDefault,
            G9ColorToken.DefaultContainer => palette.DefaultContainer,
            G9ColorToken.OnDefaultContainer => palette.OnDefaultContainer,
            G9ColorToken.DefaultBorder => palette.DefaultBorder,
            G9ColorToken.DefaultHover => palette.DefaultHover,
            G9ColorToken.DefaultPressed => palette.DefaultPressed,
            G9ColorToken.InputBackground => palette.InputBackground,
            G9ColorToken.InputBorder => palette.InputBorder,
            G9ColorToken.InputBorderFocused => palette.InputBorderFocused,
            G9ColorToken.InputPlaceholder => palette.InputPlaceholder,
            _ => null
        };

        // Null before the first G9Theme.Init() / Apply() — the palette fields start unset.
        return color ?? Colors.Magenta;
    }
}

/// <summary>
///     Registry of live <see cref="G9ColorExtension" /> subscriptions. Each entry
///     ties one target's <see cref="BindableProperty" /> to a palette key and pushes
///     the new colour on every theme change.
/// </summary>
public static class G9PaletteSubscriptions
{
    private static readonly object _lock = new();
    private static readonly List<Subscription> _subs = new();
    private static bool _attachedToPalette;

    // Dead entries are swept when the list reaches this size; see Register.
    private const int MinimumSweepThreshold = 256;
    private static int _sweepThreshold = MinimumSweepThreshold;

    public static void Register(BindableObject target, BindableProperty property, G9ColorToken key, double? alpha)
    {
        lock (_lock)
        {
            EnsureAttached();

            // Amortised sweep. Dead WeakReferences used to be pruned only while a palette event was being
            // fanned out — i.e. on a theme switch, which most sessions never perform — while every
            // {G9Color} on every page, sheet and list row ever inflated appended another entry. The list
            // grew for the life of the process. Sweeping when it reaches a threshold, and then moving the
            // threshold to twice what survived, bounds it at ~2x the live count for O(1) amortised cost
            // per registration.
            if (_subs.Count >= _sweepThreshold)
            {
                _subs.RemoveAll(static s => !s.TargetRef.TryGetTarget(out _));
                _sweepThreshold = Math.Max(MinimumSweepThreshold, _subs.Count * 2);
            }

            _subs.Add(new Subscription(new WeakReference<BindableObject>(target), property, key, alpha));
        }
    }

    private static void EnsureAttached()
    {
        if (_attachedToPalette) return;
        _attachedToPalette = true;
        G9Palette.Current.PropertyChanged += OnPaletteChanged;
    }

    private static void OnPaletteChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Empty / null name = "all properties changed" (the batch flush emits this).
        // Targeted name fires too if a single G9Palette property changes outside
        // the batch — we still re-push for that key.
        //
        // The name is resolved to a token ONCE per event. It used to be compared against
        // `sub.Key.ToString()` inside the loop — an enum-to-string conversion per entry per event.
        var allChanged = string.IsNullOrEmpty(e.PropertyName);
        var changedToken = default(G9ColorToken);
        if (!allChanged && !Enum.TryParse(e.PropertyName, out changedToken))
        {
            // Not a colour token, so no subscription can be affected.
            return;
        }

        Subscription[] snapshot;
        lock (_lock)
        {
            snapshot = _subs.ToArray();
        }

        var prunedAny = false;
        foreach (var sub in snapshot)
        {
            if (!sub.TargetRef.TryGetTarget(out var target))
            {
                prunedAny = true;
                continue;
            }

            // For empty / null property name, push every key. For a targeted change,
            // only push if it matches.
            if (!allChanged && sub.Key != changedToken)
            {
                continue;
            }

            try
            {
                var color = G9ColorExtension.ResolveColor(sub.Key, sub.Alpha);
                var newValue = ConvertForProperty(color, sub.Property);
                // Skip the SetValue call entirely when the resolved value already
                // matches what's set. SetValue on Android costs ~50 ms per call
                // because it goes through the MAUI handler's mapper which walks JNI
                // to update the platform widget. With ~80 ThemeColor markup uses on
                // a dense page that adds up — and many of them resolve to the same
                // colour (every Border that uses SurfaceContainerLowest@0.92 would
                // otherwise re-set the same brush instance).
                var current = target.GetValue(sub.Property);
                if (Equals(current, newValue)) continue;
                target.SetValue(sub.Property, newValue);
            }
            catch
            {
                // Swallow — target may have been disposed mid-fan-out.
            }
        }

        if (prunedAny)
        {
            lock (_lock)
            {
                _subs.RemoveAll(s => !s.TargetRef.TryGetTarget(out _));
            }
        }
    }

    /// <summary>
    ///     Convert the resolved <see cref="Color" /> into the value type expected by
    ///     the target <see cref="BindableProperty" />. The most common case beyond
    ///     <c>Color</c> itself is <c>Brush</c> properties (e.g. <c>Border.Stroke</c>)
    ///     which expect a <c>Brush</c> instance — XAML's binding pipeline auto-wraps
    ///     <see cref="Color" /> in a <see cref="SolidColorBrush" />, so we replicate
    ///     that here. Anything else falls through with the colour as-is and lets
    ///     MAUI's <c>BindableProperty</c> type-coerce. If even that fails the catch
    ///     above swallows it.
    /// </summary>
    private static object ConvertForProperty(Color color, BindableProperty property)
    {
        if (property.ReturnType == typeof(Brush) || typeof(Brush).IsAssignableFrom(property.ReturnType))
        {
            return new SolidColorBrush(color);
        }
        return color;
    }

    private sealed record Subscription(
        WeakReference<BindableObject> TargetRef,
        BindableProperty Property,
        G9ColorToken Key,
        double? Alpha);
}
