namespace G9MAUIControls.Theming;

public readonly record struct G9PaletteValues(
    // PRIMARY
    Color Primary,
    Color OnPrimary,
    Color PrimaryContainer,
    Color OnPrimaryContainer,
    Color PrimaryBorder,
    Color PrimaryHover,
    Color PrimaryPressed,

    // SECONDARY
    Color Secondary,
    Color OnSecondary,
    Color SecondaryContainer,
    Color OnSecondaryContainer,
    Color SecondaryBorder,

    // TERTIARY
    Color Tertiary,
    Color OnTertiary,
    Color TertiaryContainer,
    Color OnTertiaryContainer,
    Color TertiaryBorder,

    // QUATERNARY
    Color Quaternary,
    Color OnQuaternary,
    Color QuaternaryContainer,
    Color OnQuaternaryContainer,
    Color QuaternaryBorder,

    // ERROR
    Color Error,
    Color OnError,
    Color ErrorContainer,
    Color OnErrorContainer,
    Color ErrorBorder,

    // WARNING
    Color Warning,
    Color OnWarning,
    Color WarningContainer,
    Color OnWarningContainer,
    Color WarningBorder,

    // SUCCESS
    Color Success,
    Color OnSuccess,
    Color SuccessContainer,
    Color OnSuccessContainer,
    Color SuccessBorder,

    // INFO
    Color Info,
    Color OnInfo,
    Color InfoContainer,
    Color OnInfoContainer,
    Color InfoBorder,

    // OUTLINE
    Color Outline,
    Color OutlineVariant,
    Color OutlineBorder,

    // BACKGROUND
    Color Background,
    Color OnBackground,
    Color BackgroundSecondary,

    // SURFACE
    Color Surface,
    Color OnSurface,
    Color SurfaceVariant,
    Color OnSurfaceVariant,
    Color SurfaceBorder,

    // INVERSE
    Color InverseSurface,
    Color InverseOnSurface,
    Color InversePrimary,

    // SURFACE CONTAINERS
    Color SurfaceContainerHighest,
    Color SurfaceContainerHigh,
    Color SurfaceContainer,
    Color SurfaceContainerLow,
    Color SurfaceContainerLowest,
    Color SurfaceBright,
    Color SurfaceDim,

    // SCRIM & OVERLAY
    Color Scrim,
    Color ScrimLight,
    Color Overlay,
    Color OverlayLight,

    // TEXT COLORS
    Color TextPrimary,
    Color TextSecondary,
    Color TextTertiary,
    Color TextDisabled,

    // STANDARD COLORS
    Color White,
    Color Black,
    Color Light,
    Color Dark,

    // DIVIDER
    Color Divider,
    Color DividerStrong,

    // FOCUS & STATES
    Color Focus,
    Color FocusVisible,
    Color Disabled,
    Color DisabledContainer,

    // CARD
    Color CardBackground,
    Color CardBorder,
    Color CardElevated,

    // DEFAULT (non-primary button on Background/CardBackground)
    Color Default,
    Color OnDefault,
    Color DefaultContainer,
    Color OnDefaultContainer,
    Color DefaultBorder,
    Color DefaultHover,
    Color DefaultPressed,

    // INPUT
    Color InputBackground,
    Color InputBorder,
    Color InputBorderFocused,
    Color InputPlaceholder
);