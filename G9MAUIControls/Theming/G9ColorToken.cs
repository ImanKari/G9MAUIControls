namespace G9MAUIControls.Theming;

/// <summary>
///     Names MUST match the keys written into Application.Current.Resources.
/// </summary>
public enum G9ColorToken
{
    // ================= PRIMARY =================
    Primary,
    OnPrimary,
    PrimaryContainer,
    OnPrimaryContainer,
    PrimaryBorder,
    PrimaryHover,
    PrimaryPressed,

    // ================= SECONDARY =================
    Secondary,
    OnSecondary,
    SecondaryContainer,
    OnSecondaryContainer,
    SecondaryBorder,

    // ================= TERTIARY =================
    Tertiary,
    OnTertiary,
    TertiaryContainer,
    OnTertiaryContainer,
    TertiaryBorder,

    // ================= QUATERNARY =================
    Quaternary,
    OnQuaternary,
    QuaternaryContainer,
    OnQuaternaryContainer,
    QuaternaryBorder,

    // ================= ERROR =================
    Error,
    OnError,
    ErrorContainer,
    OnErrorContainer,
    ErrorBorder,

    // ================= WARNING =================
    Warning,
    OnWarning,
    WarningContainer,
    OnWarningContainer,
    WarningBorder,

    // ================= SUCCESS =================
    Success,
    OnSuccess,
    SuccessContainer,
    OnSuccessContainer,
    SuccessBorder,

    // ================= INFO =================
    Info,
    OnInfo,
    InfoContainer,
    OnInfoContainer,
    InfoBorder,

    // ================= OUTLINE =================
    Outline,
    OutlineVariant,
    OutlineBorder,

    // ================= BACKGROUND =================
    Background,
    OnBackground,
    BackgroundSecondary,

    // ================= SURFACE =================
    Surface,
    OnSurface,
    SurfaceVariant,
    OnSurfaceVariant,
    SurfaceBorder,

    // ================= INVERSE =================
    InverseSurface,
    InverseOnSurface,
    InversePrimary,

    // ================= SURFACE CONTAINERS =================
    SurfaceContainerHighest,
    SurfaceContainerHigh,
    SurfaceContainer,
    SurfaceContainerLow,
    SurfaceContainerLowest,
    SurfaceBright,
    SurfaceDim,

    // ================= SCRIM & OVERLAY =================
    Scrim,
    ScrimLight,
    Overlay,
    OverlayLight,

    // ================= TEXT COLORS =================
    TextPrimary,
    TextSecondary,
    TextTertiary,
    TextDisabled,

    // ================= STANDARD COLORS =================
    White,
    Black,
    Light,
    Dark,

    // ================= DIVIDER =================
    Divider,
    DividerStrong,

    // ================= FOCUS & STATES =================
    Focus,
    FocusVisible,
    Disabled,
    DisabledContainer,

    // ================= CARD =================
    CardBackground,
    CardBorder,
    CardElevated,

    // ================= DEFAULT (non-primary button on Background/CardBackground) =================
    Default,
    OnDefault,
    DefaultContainer,
    OnDefaultContainer,
    DefaultBorder,
    DefaultHover,
    DefaultPressed,

    // ================= INPUT =================
    InputBackground,
    InputBorder,
    InputBorderFocused,
    InputPlaceholder
}