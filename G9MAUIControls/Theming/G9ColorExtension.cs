using System.ComponentModel;
using System.Reflection;
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
///         disposed. The subscription is implicitly cleaned up when
///         <see cref="G9Palette.Current" />'s reference list cleans up dead weak
///         references on each fan-out.
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

    private static readonly Dictionary<G9ColorToken, PropertyInfo> _propertyCache = new();

    /// <summary>
    ///     Look up the <see cref="G9Palette" /> property by enum-name match. The
    ///     reflection cost is paid once per key and cached; subsequent reads are a
    ///     plain dictionary lookup + property getter.
    /// </summary>
    private static Color ReadPaletteColor(G9ColorToken key)
    {
        if (!_propertyCache.TryGetValue(key, out var prop))
        {
            prop = typeof(G9Palette).GetProperty(key.ToString(), BindingFlags.Public | BindingFlags.Instance);
            if (prop is not null)
            {
                _propertyCache[key] = prop;
            }
        }
        return prop?.GetValue(G9Palette.Current) as Color ?? Colors.Magenta;
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

    public static void Register(BindableObject target, BindableProperty property, G9ColorToken key, double? alpha)
    {
        lock (_lock)
        {
            EnsureAttached();
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
            if (!string.IsNullOrEmpty(e.PropertyName)
                && !string.Equals(e.PropertyName, sub.Key.ToString(), StringComparison.Ordinal))
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
