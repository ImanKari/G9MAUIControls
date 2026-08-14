# G9MAUIControls.IntroCarousel

Onboarding carousel with video slides, for [`G9MAUIControls`](https://www.nuget.org/packages/G9MAUIControls).

```
dotnet add package G9MAUIControls.IntroCarousel
```

## What it adds

`G9IntroCarousel` — the first-run walkthrough control: swipeable slides that can be an image **or a
video**, page indicators, a skip affordance, and paging that reverses correctly in RTL.

## Why this is a separate package

Video playback needs `CommunityToolkit.Maui.MediaElement`, which pulls a platform media stack — ExoPlayer
on Android, AVPlayer on Apple. That is a large dependency to hand to somebody who only wanted a text
entry, so it lives here with the one control that needs it.

**What you take on by installing this:** `CommunityToolkit.Maui.MediaElement` and its platform players,
plus `builder.UseMauiCommunityToolkitMediaElement()` in `MauiProgram`.

## The teardown order you must not "simplify"

The carousel disconnects the media handler itself, in a specific sequence, and the sequence is
load-bearing. On Android, if the native media view is disposed by the fragment's own `onDestroyView`
**before** the visual-diagnostics overlay is de-initialised, the overlay later calls `RemoveView` on a
disposed parent and the app crashes. The control therefore de-initialises the overlay *first*, while the
native view is still alive, then stops playback, then lets MAUI disconnect.

It is commented at the site. Read that comment before changing anything in the teardown path.

## Usage

```xml
<intro:G9IntroCarousel
    x:Name="Intro"
    Slides="{Binding Slides}"
    CurrentIndex="{Binding Index}"
    CompleteCommand="{Binding FinishOnboardingCommand}"
    LanguageCommand="{Binding SwitchLanguageCommand}"
    UseGradientOverlay="True" />
```

```csharp
Slides =
[
    new G9IntroSlideItem
    {
        TitleResourceKey    = "Intro_Welcome_Title",
        SubtitleResourceKey = "Intro_Welcome_Body",
        ImageSource         = "slide1.png"
    },
    new G9IntroSlideItem
    {
        TitleResourceKey    = "Intro_Scanning_Title",
        SubtitleResourceKey = "Intro_Scanning_Body",
        VideoAssetPath      = "Onboarding/scan.mp4",   // MauiAsset logical path
        ImageSource         = "slide2.png"             // fallback if playback fails
    }
];
```

**Two things this control will not do for you.**

**1. Slide copy is resolved through your string catalogue, by key.** `TitleResourceKey` and
`SubtitleResourceKey` go through `G9Strings.Resolve`, so wire the core's string hook —
`G9Strings.UseResources(MyResources.ResourceManager)` — or every slide renders blank. The carousel ships
no default copy, because onboarding text is the most app-specific content there is.

**2. Nothing plays until you call `BeginPresentation()`.** Media and chrome stay idle until then, on
purpose: onboarding usually sits behind a bootstrap or a login screen, and starting video decode while
the app is still starting up costs you the frames you least want to lose.

```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    Intro.BeginPresentation();
}
```

Call `StopAndReleaseMedia()` when you navigate away. The `FirstContentReady` event fires when the first
slide has something on screen — useful for dismissing your own splash without a flash of empty carousel.

Video paths resolve through `G9IntroMediaResolver`, which copies a packaged asset out to a readable file
path — `MediaElement` cannot play straight out of the app package.

## Requirements

.NET 10 · `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`

## License

MIT
