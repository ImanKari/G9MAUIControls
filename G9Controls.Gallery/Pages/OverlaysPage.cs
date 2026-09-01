using G9MAUIControls.BottomSheet;
using G9MAUIControls.Controls;
using G9MAUIControls.Hosting;
using G9MAUIControls.Popup;
using G9MAUIControls.ProgressOverlay;
using G9MAUIControls.Theming;
using G9MAUIControls.Toast;

namespace G9Controls.Gallery.Pages;

/// <summary>
///     The overlay z-stack, which is the contract most likely to be silently broken by the extraction.
///     <para>
///         Sibling order in <c>G9PageTemplate.xaml</c> IS the z-order, so these buttons exist to prove the
///         three claims that ordering makes: a popup paints above an open sheet, a toast paints above both,
///         and a toast opened from inside a sheet keeps showing after the sheet closes.
///     </para>
///     <para>
///         The progress overlay is here too, because it is the first thing to use the public
///         <c>IG9OverlayHost</c> seam — if that seam is wrong, this page is where it shows.
///     </para>
/// </summary>
public sealed class OverlaysPage : G9PageBase
{
    public OverlaysPage()
    {
        Title = "Overlays";
        Content = Build();

        // Proves the cancel channel end to end: the overlay broadcasts, the app decides what to stop.
        G9ProgressOverlayHelper.CancelRequested += OnProgressCancelRequested;
    }

    private CancellationTokenSource? _runCts;

    private void OnProgressCancelRequested(object? sender, G9ProgressCancelRequested e)
    {
        _runCts?.Cancel();
    }

    private View Build()
    {
        var palette = G9Palette.Current;
        var stack = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(16) };

        stack.Add(new Label
        {
            Text = "Z-STACK: run 'Sheet, then popup, then toast' and confirm each paints above the previous. "
                 + "Then 'Toast from inside a sheet' and close the sheet — the toast must survive.",
            FontSize = 12,
            TextColor = palette.OnSurfaceVariant
        });

        stack.Add(Button("Toast — success", () => G9ToastHelper.ShowToastAsync("Saved", G9ToastType.Success)));
        stack.Add(Button("Toast — error", () => G9ToastHelper.ShowToastAsync(
            "Could not reach the server", G9ToastType.Error)));
        stack.Add(Button("Toast — stack four", async () =>
        {
            for (var i = 1; i <= 4; i++)
            {
                await G9ToastHelper.ShowToastAsync($"Toast {i}", G9ToastType.Information);
            }
        }));

        stack.Add(Button("Popup — information", () => G9PopupHelper.ShowG9PopupAsync("A neutral message.", "Information")));
        stack.Add(Button("Popup — success", () => G9PopupHelper.ShowSuccessG9PopupAsync("That worked.", "Success")));
        stack.Add(Button("Popup — warning", () => G9PopupHelper.ShowWarningG9PopupAsync("Careful.", "Warning")));
        stack.Add(Button("Popup — error", () => G9PopupHelper.ShowErrorG9PopupAsync("That failed.", "Error")));
        stack.Add(Button("Popup — confirm", () => G9PopupHelper.ShowConfirmAsync("Turn off all relays?", "Confirm")));
        stack.Add(Button("Popup — input form", () => G9PopupHelper.ShowInputG9PopupAsync(new G9PopupInputOptions
        {
            Title = "Quick input",
            Fields =
            [
                new G9PopupInputField { Key = "name", Label = "Name", IsRequired = true },
                new G9PopupInputField { Key = "note", Label = "Note" }
            ]
        })));

        stack.Add(Button("Sheet — fit to content", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(SheetBody(palette));
            return Task.CompletedTask;
        }));

        stack.Add(Button("Sheet — peek, drag to FIT (short body)", () =>
        {
            ShowExpandingSheet(palette, groupCount: 3);
            return Task.CompletedTask;
        }));

        stack.Add(Button("Sheet — peek, drag to CAP + scroll (tall body)", () =>
        {
            ShowExpandingSheet(palette, groupCount: 14);
            return Task.CompletedTask;
        }));

        stack.Add(Button("Sheet, then popup, then toast", async () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(SheetBody(palette));
            await Task.Delay(600);
            _ = G9PopupHelper.ShowG9PopupAsync("This must paint ABOVE the sheet.", "Popup over sheet");
            await Task.Delay(600);
            await G9ToastHelper.ShowToastAsync("And this above BOTH.", G9ToastType.Information);
        }));

        stack.Add(Button("Toast from inside a sheet (then close it)", () =>
        {
            var body = new VerticalStackLayout
            {
                Spacing = 12,
                Padding = new Thickness(16),
                Children =
                {
                    new Label { Text = "Tap the button, then close this sheet.", TextColor = palette.OnSurface },
                    Button("Raise a toast", () => G9ToastHelper.ShowToastAsync(
                        "I must outlive the sheet.", G9ToastType.Success))
                }
            };
            G9BottomSheetHelper.ShowG9BottomSheet(body);
            return Task.CompletedTask;
        }));

        stack.Add(Button("Progress overlay — run, then succeed", async () =>
        {
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            var token = _runCts.Token;

            // The handle is the LEASE, not the overlay: disposing it releases one claim, and the overlay
            // tears down when the last claim goes. Concurrent operations therefore share one visual.
            await using var handle = await G9ProgressOverlayHelper.ShowAsync("Uploading samples");

            for (var i = 0; i <= 10 && !token.IsCancellationRequested; i++)
            {
                G9ProgressOverlayHelper.Report(i / 10d, "Uploading", $"{i * 25} of 250");
                await Task.Delay(220, CancellationToken.None);
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            await G9ProgressOverlayHelper.TryShowCurrentSuccessAsync("250 samples uploaded", "All batches accepted");
        }));

        stack.Add(Button("Progress overlay — indeterminate, then fail with retry", async () =>
        {
            await using var handle = await G9ProgressOverlayHelper.ShowAsync("Syncing");

            G9ProgressOverlayHelper.Report(G9ProgressReport.Indeterminate("Connecting"));
            G9ProgressOverlayHelper.ReportQueued(3);
            await Task.Delay(1200);

            await G9ProgressOverlayHelper.TryShowCurrentFailureAsync(
                "Server unreachable",
                "Retry",
                () => G9ToastHelper.ShowToastAsync("Retry tapped", G9ToastType.Information));
        }));

        stack.Add(Button("Progress overlay — top anchored", async () =>
        {
            await using var handle = await G9ProgressOverlayHelper.ShowAsync(
                "Anchored to the top edge", G9ProgressOverlayPosition.Top);
            for (var i = 0; i <= 5; i++)
            {
                G9ProgressOverlayHelper.Report(i / 5d, "Working");
                await Task.Delay(300);
            }
        }));

        stack.Add(Button("Progress overlay — standalone failure (no run)", () =>
            G9ProgressOverlayHelper.ShowStandaloneFailureAsync(
                "A background sync failed with nothing on screen",
                "Retry",
                () => G9ToastHelper.ShowToastAsync("Retried", G9ToastType.Information))));

        stack.Add(Button("Loading — full screen, 2s", async () =>
        {
            await G9ToastHelper.ShowLoadingAsync("Signing in…");
            await Task.Delay(2000);
            await G9ToastHelper.DismissLoadingAsync();
        }));

        stack.Add(Button("Dismiss everything", () => G9ToastHelper.DismissAllAsync()));

        return new ScrollView { Content = stack };
    }

    /// <summary>
    ///     The two-detent sheet: opens at a peek, drags open to the CONTENT's own height, and stops
    ///     at the cap with the body scrolling when the content is taller than the cap.
    /// </summary>
    /// <remarks>
    ///     Run both buttons and check four things, because each one is a separate rule and three of
    ///     them were broken before <c>ExpandedFitsContent</c> / <c>ScrollingExpandsSheet</c> existed:
    ///     <list type="number">
    ///         <item>SHORT body — dragging up settles exactly under the last row. No empty band, and
    ///         the sheet cannot be dragged past it up to the status bar.</item>
    ///         <item>TALL body — dragging up stops at 85% of the screen (the cap) and the body then
    ///         scrolls inside it.</item>
    ///         <item>At the PEEK step the body does not scroll at all: the same upward drag expands
    ///         the sheet instead. That is <c>ScrollingExpandsSheet</c>.</item>
    ///         <item>From the top, dragging down at the scroller's top edge steps back to the peek;
    ///         dragging down again from the peek dismisses.</item>
    ///     </list>
    /// </remarks>
    private static void ShowExpandingSheet(G9Palette palette, int groupCount)
    {
        var body = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(16, 12, 16, 20) };

        body.Add(new Label
        {
            Text = groupCount > 6 ? "Taller than the cap — scrolls at the top" : "Shorter than the cap — fits exactly",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = palette.OnSurface
        });

        for (var i = 1; i <= groupCount; i++)
        {
            body.Add(new Label
            {
                Text = $"Group {i}",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = palette.OnSurfaceVariant
            });

            var row = new HorizontalStackLayout { Spacing = 8 };
            for (var tile = 0; tile < 4; tile++)
            {
                row.Add(new Border
                {
                    WidthRequest = 72,
                    HeightRequest = 72,
                    StrokeThickness = 0,
                    BackgroundColor = tile % 2 == 0 ? palette.SurfaceVariant : palette.Surface,
                    Content = new Label
                    {
                        Text = $"{i}.{tile + 1}",
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        TextColor = palette.OnSurfaceVariant
                    }
                });
            }

            body.Add(row);
        }

        G9BottomSheetHelper.ShowG9BottomSheet(body, G9BottomSheetOptions.DefaultOptions() with
        {
            SizeMode = G9BottomSheetSizeMode.States,
            CurrentState = G9BottomSheetState.Peek,
            States = [G9BottomSheetState.Peek, G9BottomSheetState.Medium],
            PeekHeight = 260,
            CollapsedHeight = 260,
            ExpandedFitsContent = true,
            MaxFitToContentHeightRatio = 0.85,
            DeferContent = false
        });
    }

    private static View SheetBody(G9Palette palette) => new VerticalStackLayout
    {
        Spacing = 10,
        Padding = new Thickness(16),
        Children =
        {
            new Label
            {
                Text = "Fit-to-content sheet",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = palette.OnSurface
            },
            new Label
            {
                Text = "A fit-to-content body must NOT be wrapped in a ScrollView and must not use a "
                     + "greedy * row — either reports its viewport and the sheet opens too small.",
                FontSize = 12,
                TextColor = palette.OnSurfaceVariant
            },
            new G9Button
            {
                Text = "Close",
                Variant = G9ButtonVariant.Outline,
                Command = new Command(() => G9BottomSheetHelper.CloseG9BottomSheet())
            }
        }
    };

    private static G9Button Button(string text, Func<Task> action) => new()
    {
        Text = text,
        Variant = G9ButtonVariant.Primary,
        Command = new Command(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                // The gallery must survive a broken overlay so the rest of it stays testable.
                await G9ToastHelper.ShowToastAsync(ex.Message, G9ToastType.Error);
            }
        })
    };

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // CancelRequested holds handlers strongly (documented on the event), so a page that subscribes must
        // release on teardown or it keeps this page — and its whole visual tree — alive.
        if (Handler is null)
        {
            G9ProgressOverlayHelper.CancelRequested -= OnProgressCancelRequested;
            _runCts?.Dispose();
            _runCts = null;
        }
    }
}
