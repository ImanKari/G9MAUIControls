using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace G9MAUIControls.Theming;

public partial class G9Palette : ObservableObject
{
    /// <summary>
    ///     Suppresses individual <see cref="ObservableObject.PropertyChanged" /> notifications
    ///     while a batch palette update is in progress. <see cref="G9Theme.ApplyCurrent" />
    ///     mutates ~100 color properties back-to-back; without batching every setter fires
    ///     a <c>PropertyChanged</c> event that fans out to every <see cref="G9ColorExtension" />
    ///     binding in the visual tree (hundreds on a dense page like
    ///     <c>TempDesignPageIman</c>). The compounded cost was measured at <b>over 240
    ///     seconds</b> on Android emulator-5554 — far past any acceptable interactive
    ///     budget. Batching defers the actual <c>PropertyChanged</c> fan-out until
    ///     <see cref="EndBatchUpdate" /> at which point each property whose value
    ///     actually changed during the batch fires exactly one notification.
    /// </summary>
    private bool _batchUpdateInProgress;

    /// <summary>
    ///     Names of properties whose setter ran while a batch was open. Captured during
    ///     batching so <see cref="EndBatchUpdate" /> can fire targeted
    ///     <c>PropertyChanged</c> events — much cheaper than a single
    ///     <c>OnPropertyChanged(string.Empty)</c> "everything changed" fan-out, which
    ///     forces every binding to re-evaluate its full path on every property the
    ///     palette exposes.
    /// </summary>
    private readonly HashSet<string> _batchedPropertyNames = new(StringComparer.Ordinal);
    // ================= BACKGROUND =================
    [ObservableProperty] private Color _background = null!;
    [ObservableProperty] private Color _backgroundSecondary = null!;
    [ObservableProperty] private Color _black = null!;

    // ================= CARD =================
    [ObservableProperty] private Color _cardBackground = null!;
    [ObservableProperty] private Color _cardBorder = null!;
    [ObservableProperty] private Color _cardElevated = null!;
    [ObservableProperty] private Color _dark = null!;

    // ================= DEFAULT (non-primary button) =================
    [ObservableProperty] private Color _default = null!;
    [ObservableProperty] private Color _onDefault = null!;
    [ObservableProperty] private Color _defaultContainer = null!;
    [ObservableProperty] private Color _onDefaultContainer = null!;
    [ObservableProperty] private Color _defaultBorder = null!;
    [ObservableProperty] private Color _defaultHover = null!;
    [ObservableProperty] private Color _defaultPressed = null!;
    [ObservableProperty] private Color _disabled = null!;
    [ObservableProperty] private Color _disabledContainer = null!;

    // ================= DIVIDER =================
    [ObservableProperty] private Color _divider = null!;
    [ObservableProperty] private Color _dividerStrong = null!;

    // ================= ERROR =================
    [ObservableProperty] private Color _error = null!;
    [ObservableProperty] private Color _errorBorder = null!;
    [ObservableProperty] private Color _errorContainer = null!;

    // ================= FOCUS & STATES =================
    [ObservableProperty] private Color _focus = null!;
    [ObservableProperty] private Color _focusVisible = null!;

    // ================= INFO =================
    [ObservableProperty] private Color _info = null!;
    [ObservableProperty] private Color _infoBorder = null!;
    [ObservableProperty] private Color _infoContainer = null!;

    // ================= INPUT =================
    [ObservableProperty] private Color _inputBackground = null!;
    [ObservableProperty] private Color _inputBorder = null!;
    [ObservableProperty] private Color _inputBorderFocused = null!;
    [ObservableProperty] private Color _inputPlaceholder = null!;
    [ObservableProperty] private Color _inverseOnSurface = null!;
    [ObservableProperty] private Color _inversePrimary = null!;

    // ================= INVERSE =================
    [ObservableProperty] private Color _inverseSurface = null!;
    [ObservableProperty] private Color _light = null!;
    [ObservableProperty] private Color _onBackground = null!;
    [ObservableProperty] private Color _onError = null!;
    [ObservableProperty] private Color _onErrorContainer = null!;
    [ObservableProperty] private Color _onInfo = null!;
    [ObservableProperty] private Color _onInfoContainer = null!;
    [ObservableProperty] private Color _onPrimary = null!;
    [ObservableProperty] private Color _onPrimaryContainer = null!;
    [ObservableProperty] private Color _onSecondary = null!;
    [ObservableProperty] private Color _onSecondaryContainer = null!;
    [ObservableProperty] private Color _onSuccess = null!;
    [ObservableProperty] private Color _onSuccessContainer = null!;
    [ObservableProperty] private Color _onSurface = null!;
    [ObservableProperty] private Color _onSurfaceVariant = null!;
    [ObservableProperty] private Color _onTertiary = null!;
    [ObservableProperty] private Color _onTertiaryContainer = null!;
    [ObservableProperty] private Color _onWarning = null!;
    [ObservableProperty] private Color _onWarningContainer = null!;

    // ================= OUTLINE =================
    [ObservableProperty] private Color _outline = null!;
    [ObservableProperty] private Color _outlineBorder = null!;
    [ObservableProperty] private Color _outlineVariant = null!;
    [ObservableProperty] private Color _overlay = null!;

    [ObservableProperty] private Color _overlayLight = null!;

    // ================= QUATERNARY =================
    [ObservableProperty] private Color _quaternary = null!;
    [ObservableProperty] private Color _onQuaternary = null!;
    [ObservableProperty] private Color _quaternaryContainer = null!;
    [ObservableProperty] private Color _onQuaternaryContainer = null!;
    [ObservableProperty] private Color _quaternaryBorder = null!;

    // ================= PRIMARY =================
    [ObservableProperty] private Color _primary = null!;
    [ObservableProperty] private Color _primaryBorder = null!;
    [ObservableProperty] private Color _primaryContainer = null!;
    [ObservableProperty] private Color _primaryHover = null!;
    [ObservableProperty] private Color _primaryPressed = null!;

    // ================= SCRIM & OVERLAY =================
    [ObservableProperty] private Color _scrim = null!;
    [ObservableProperty] private Color _scrimLight = null!;

    // ================= SECONDARY =================
    [ObservableProperty] private Color _secondary = null!;
    [ObservableProperty] private Color _secondaryBorder = null!;
    [ObservableProperty] private Color _secondaryContainer = null!;

    // (No SHADOW tokens: the app is shadow-free by policy — see G9Controls.md → "No
    // shadows". The two remaining Skia-painted shadows hard-code black, which is what these
    // tokens resolved to in both dictionaries.)

    // ================= SUCCESS =================
    [ObservableProperty] private Color _success = null!;
    [ObservableProperty] private Color _successBorder = null!;
    [ObservableProperty] private Color _successContainer = null!;

    // ================= SURFACE =================
    [ObservableProperty] private Color _surface = null!;
    [ObservableProperty] private Color _surfaceBorder = null!;
    [ObservableProperty] private Color _surfaceBright = null!;
    [ObservableProperty] private Color _surfaceContainer = null!;
    [ObservableProperty] private Color _surfaceContainerHigh = null!;

    // ================= SURFACE CONTAINERS =================
    [ObservableProperty] private Color _surfaceContainerHighest = null!;
    [ObservableProperty] private Color _surfaceContainerLow = null!;
    [ObservableProperty] private Color _surfaceContainerLowest = null!;
    [ObservableProperty] private Color _surfaceDim = null!;
    [ObservableProperty] private Color _surfaceVariant = null!;

    // ================= TERTIARY =================
    [ObservableProperty] private Color _tertiary = null!;
    [ObservableProperty] private Color _tertiaryBorder = null!;
    [ObservableProperty] private Color _tertiaryContainer = null!;
    [ObservableProperty] private Color _textDisabled = null!;

    // ================= TEXT COLORS =================
    [ObservableProperty] private Color _textPrimary = null!;
    [ObservableProperty] private Color _textSecondary = null!;
    [ObservableProperty] private Color _textTertiary = null!;

    // ================= WARNING =================
    [ObservableProperty] private Color _warning = null!;
    [ObservableProperty] private Color _warningBorder = null!;
    [ObservableProperty] private Color _warningContainer = null!;

    // ================= STANDARD COLORS =================
    [ObservableProperty] private Color _white = null!;

    // Global palette instance (like a theme service)
    public static G9Palette Current { get; } = new();

    /// <summary>
    ///     Open a batch palette mutation. While open, individual property setters do
    ///     not raise <see cref="ObservableObject.PropertyChanged" /> events to bound
    ///     consumers. Pair every call with <see cref="EndBatchUpdate" />; the
    ///     <c>EndBatchUpdate</c> call flushes a single "all properties changed"
    ///     notification (using <see cref="string.Empty" /> as the property name, the
    ///     INPC convention for "everything is invalidated"), causing every binding
    ///     to re-resolve in one pass instead of N.
    /// </summary>
    public void BeginBatchUpdate()
    {
        _batchUpdateInProgress = true;
        _batchedPropertyNames.Clear();
    }

    /// <summary>
    ///     Close a batch palette mutation and emit a single
    ///     <c>PropertyChanged(string.Empty)</c> "all properties changed" notification.
    ///     See <see cref="BeginBatchUpdate" /> for rationale. The empty-string event is
    ///     <b>much</b> cheaper than firing one targeted event per changed property —
    ///     a single empty event causes MAUI's binding pipeline to re-resolve every
    ///     binding listening on this object exactly once, while N targeted events
    ///     would multiply that fan-out by N.
    /// </summary>
    public void EndBatchUpdate()
    {
        _batchUpdateInProgress = false;
        _batchedPropertyNames.Clear();
        // Empty / null property name = "everything changed" per the INotifyPropertyChanged
        // contract. Each binding observing this object will re-read its target property
        // exactly once.
        base.OnPropertyChanged(new PropertyChangedEventArgs(string.Empty));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_batchUpdateInProgress)
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                _batchedPropertyNames.Add(e.PropertyName);
            }
            return;
        }
        base.OnPropertyChanged(e);
    }

    public void Apply(G9PaletteValues v)
    {
        // PRIMARY
        Primary = v.Primary;
        OnPrimary = v.OnPrimary;
        PrimaryContainer = v.PrimaryContainer;
        OnPrimaryContainer = v.OnPrimaryContainer;
        PrimaryBorder = v.PrimaryBorder;
        PrimaryHover = v.PrimaryHover;
        PrimaryPressed = v.PrimaryPressed;

        // SECONDARY
        Secondary = v.Secondary;
        OnSecondary = v.OnSecondary;
        SecondaryContainer = v.SecondaryContainer;
        OnSecondaryContainer = v.OnSecondaryContainer;
        SecondaryBorder = v.SecondaryBorder;

        // TERTIARY
        Tertiary = v.Tertiary;
        OnTertiary = v.OnTertiary;
        TertiaryContainer = v.TertiaryContainer;
        OnTertiaryContainer = v.OnTertiaryContainer;
        TertiaryBorder = v.TertiaryBorder;

        // QUATERNARY
        Quaternary = v.Quaternary;
        OnQuaternary = v.OnQuaternary;
        QuaternaryContainer = v.QuaternaryContainer;
        OnQuaternaryContainer = v.OnQuaternaryContainer;
        QuaternaryBorder = v.QuaternaryBorder;

        // ERROR
        Error = v.Error;
        OnError = v.OnError;
        ErrorContainer = v.ErrorContainer;
        OnErrorContainer = v.OnErrorContainer;
        ErrorBorder = v.ErrorBorder;

        // WARNING
        Warning = v.Warning;
        OnWarning = v.OnWarning;
        WarningContainer = v.WarningContainer;
        OnWarningContainer = v.OnWarningContainer;
        WarningBorder = v.WarningBorder;

        // SUCCESS
        Success = v.Success;
        OnSuccess = v.OnSuccess;
        SuccessContainer = v.SuccessContainer;
        OnSuccessContainer = v.OnSuccessContainer;
        SuccessBorder = v.SuccessBorder;

        // INFO
        Info = v.Info;
        OnInfo = v.OnInfo;
        InfoContainer = v.InfoContainer;
        OnInfoContainer = v.OnInfoContainer;
        InfoBorder = v.InfoBorder;

        // OUTLINE
        Outline = v.Outline;
        OutlineVariant = v.OutlineVariant;
        OutlineBorder = v.OutlineBorder;

        // BACKGROUND
        Background = v.Background;
        OnBackground = v.OnBackground;
        BackgroundSecondary = v.BackgroundSecondary;

        // SURFACE
        Surface = v.Surface;
        OnSurface = v.OnSurface;
        SurfaceVariant = v.SurfaceVariant;
        OnSurfaceVariant = v.OnSurfaceVariant;
        SurfaceBorder = v.SurfaceBorder;

        // INVERSE
        InverseSurface = v.InverseSurface;
        InverseOnSurface = v.InverseOnSurface;
        InversePrimary = v.InversePrimary;

        // SURFACE CONTAINERS
        SurfaceContainerHighest = v.SurfaceContainerHighest;
        SurfaceContainerHigh = v.SurfaceContainerHigh;
        SurfaceContainer = v.SurfaceContainer;
        SurfaceContainerLow = v.SurfaceContainerLow;
        SurfaceContainerLowest = v.SurfaceContainerLowest;
        SurfaceBright = v.SurfaceBright;
        SurfaceDim = v.SurfaceDim;

        // SCRIM & OVERLAY
        Scrim = v.Scrim;
        ScrimLight = v.ScrimLight;
        Overlay = v.Overlay;
        OverlayLight = v.OverlayLight;

        // TEXT COLORS
        TextPrimary = v.TextPrimary;
        TextSecondary = v.TextSecondary;
        TextTertiary = v.TextTertiary;
        TextDisabled = v.TextDisabled;

        // STANDARD COLORS
        White = v.White;
        Black = v.Black;
        Light = v.Light;
        Dark = v.Dark;

        // DIVIDER
        Divider = v.Divider;
        DividerStrong = v.DividerStrong;

        // FOCUS & STATES
        Focus = v.Focus;
        FocusVisible = v.FocusVisible;
        Disabled = v.Disabled;
        DisabledContainer = v.DisabledContainer;

        // CARD
        CardBackground = v.CardBackground;
        CardBorder = v.CardBorder;
        CardElevated = v.CardElevated;

        // DEFAULT (non-primary button)
        Default = v.Default;
        OnDefault = v.OnDefault;
        DefaultContainer = v.DefaultContainer;
        OnDefaultContainer = v.OnDefaultContainer;
        DefaultBorder = v.DefaultBorder;
        DefaultHover = v.DefaultHover;
        DefaultPressed = v.DefaultPressed;

        // INPUT
        InputBackground = v.InputBackground;
        InputBorder = v.InputBorder;
        InputBorderFocused = v.InputBorderFocused;
        InputPlaceholder = v.InputPlaceholder;
    }
}