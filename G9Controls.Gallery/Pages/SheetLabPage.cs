using G9MAUIControls.BottomSheet;
using G9MAUIControls.Controls;
using G9MAUIControls.Hosting;
using G9MAUIControls.Icons;
using G9MAUIControls.Theming;
using G9MAUIControls.Toast;

namespace G9Controls.Gallery.Pages;

/// <summary>
///     The regression surface for the bottom-sheet engine — one button per behaviour, each written
///     around what is supposed to be TRUE when you press it rather than around what is easy to show.
///     <para>
///         Every case states its expectation on the button's own caption, because the failures this
///         page exists to catch are visual and easy to miss: a sheet that opens at the wrong height
///         and corrects itself, content that arrives a beat after the sheet, a footer that leaves
///         the screen edge during a drag, a resize that stutters. Press, and LOOK.
///     </para>
///     <para>
///         The switches at the top flip the engine between its staged pipeline and the legacy one
///         at runtime, so the two can be compared on the same device in the same minute — which is
///         the only comparison worth trusting.
///     </para>
/// </summary>
public sealed class SheetLabPage : G9PageBase
{
    private G9BottomSheetSettings _settings = G9BottomSheetSettings.Default;
    private Label? _settingsLabel;

    public SheetLabPage()
    {
        Title = "Sheet Lab";
        Content = new ScrollView { Content = Build() };
    }

    private View Build()
    {
        var palette = G9Palette.Current;
        var stack = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(16) };

        stack.Add(Caption(
            "EXPECT, for every sheet on this page: it rises ONCE, at its final height, with its content "
            + "already in it. No skeleton unless the case says so, no second size change, no icons arriving late.",
            palette));

        // ── engine switches ─────────────────────────────────────────────────────────────────────
        _settingsLabel = Caption(string.Empty, palette);
        stack.Add(_settingsLabel);

        stack.Add(Button("Engine: toggle STAGE-BEFORE-SHOW (off = legacy pipeline)", () =>
        {
            Apply(_settings with { StageBeforeShow = !_settings.StageBeforeShow });
            return Task.CompletedTask;
        }));

        stack.Add(Button("Engine: cycle settle frames 0 → 2 → 6", () =>
        {
            var next = _settings.PreOpenSettleFrames switch { 0 => 2, 2 => 6, _ => 0 };
            Apply(_settings with { PreOpenSettleFrames = next });
            return Task.CompletedTask;
        }));

        stack.Add(Button("Engine: toggle hardware layer during motion (Android)", () =>
        {
            Apply(_settings with { UseHardwareLayerDuringMotion = !_settings.UseHardwareLayerDuringMotion });
            return Task.CompletedTask;
        }));

        stack.Add(Button("Motion: toggle PLATFORM-NATIVE ↔ timed (legacy CubicOut)", () =>
        {
            Apply(_settings with
            {
                MotionStyle = _settings.MotionStyle == G9SheetMotionStyle.PlatformNative
                    ? G9SheetMotionStyle.Timed
                    : G9SheetMotionStyle.PlatformNative
            });
            return Task.CompletedTask;
        }));

        stack.Add(Button("Motion: toggle frame-clock driver (Android)", () =>
        {
            Apply(_settings with { UseFrameClockMotion = !_settings.UseFrameClockMotion });
            return Task.CompletedTask;
        }));

        Apply(_settings);

        // ── first-open height ───────────────────────────────────────────────────────────────────
        stack.Add(Section("First-open height (data already in hand)", palette));

        stack.Add(Button("Light menu — 6 rows. EXPECT: exact height, first frame", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(Menu(6, palette), TitledFit("Operations"));
            return Task.CompletedTask;
        }));

        stack.Add(Button("Two-row menu. EXPECT: a SHORT sheet, not inflated to a floor", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(Menu(2, palette), TitledFit("Short"));
            return Task.CompletedTask;
        }));

        stack.Add(Button("Tall body in a ScrollView. EXPECT: opens at the 75% cap and scrolls", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(
                new ScrollView { Content = Menu(40, palette) },
                TitledFit("Tall"));
            return Task.CompletedTask;
        }));

        stack.Add(Button("Wrapping text + footer. EXPECT: footer fully visible, nothing clipped", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(WrappingBody(palette), TitledFit("Wrapping") with
            {
                FooterButtons = [FooterButton("Cancel", G9ButtonVariant.Outline), FooterButton("Save", G9ButtonVariant.Primary)]
            });
            return Task.CompletedTask;
        }));

        // ── resize ──────────────────────────────────────────────────────────────────────────────
        stack.Add(Section("Resize after open (one layout pass, then pure translation)", palette));

        stack.Add(Button("Grow / shrink with a sticky footer. EXPECT: smooth, footer welded to the edge", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(ResizableBody(palette), TitledFit("Resize") with
            {
                FooterButtons = [FooterButton("Done", G9ButtonVariant.Primary)]
            });
            return Task.CompletedTask;
        }));

        // ── detents and gestures ────────────────────────────────────────────────────────────────
        stack.Add(Section("Detents and gestures", palette));

        stack.Add(Button("Medium ↔ Large with a footer. EXPECT: footer stays at the edge WHILE dragging", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(Menu(8, palette), G9BottomSheetOptions.DefaultOptions() with
            {
                ShowToolbar = true,
                ShowCloseButton = true,
                Title = "Detents",
                DeferContent = false,
                FooterButtons = [FooterButton("Apply", G9ButtonVariant.Primary)]
            });
            return Task.CompletedTask;
        }));

        stack.Add(Caption(
            "On that sheet: a SHORT FAST flick up must expand it and a short fast flick down must collapse it "
            + "(it used to spring back unless dragged 100 dp). Touch it while it is still rising — it must stop "
            + "under the finger, not shudder.",
            palette));

        // ── pickers ─────────────────────────────────────────────────────────────────────────────
        stack.Add(Section("List picker honours the caller's sizing", palette));

        stack.Add(Button("Fit picker — 5 items. EXPECT: five rows tall, NOT full screen", async () =>
        {
            var picked = await G9BottomSheetHelper.ShowListG9BottomSheetAsync(
                "Pick one",
                Items(5),
                options: G9BottomSheetOptions.FitToContentOptions());
            await G9ToastHelper.ShowToastAsync($"Returned {picked.Count} item(s)", G9ToastType.Information);
        }));

        stack.Add(Button("Fit picker — 60 items. EXPECT: stops at the cap and scrolls", async () =>
        {
            var picked = await G9BottomSheetHelper.ShowListG9BottomSheetAsync(
                "Pick one",
                Items(60),
                options: G9BottomSheetOptions.FitToContentOptions());
            await G9ToastHelper.ShowToastAsync($"Returned {picked.Count} item(s)", G9ToastType.Information);
        }));

        stack.Add(Button("Default picker (no options). EXPECT: full screen, as before", async () =>
        {
            var picked = await G9BottomSheetHelper.ShowListG9BottomSheetAsync("Pick one", Items(60));
            await G9ToastHelper.ShowToastAsync($"Returned {picked.Count} item(s)", G9ToastType.Information);
        }));

        // ── deferred content and failure paths ──────────────────────────────────────────────────
        stack.Add(Section("Factory content and failures", palette));

        stack.Add(Button("Factory body with a skeleton. EXPECT: build starts when the open motion ENDS", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(() => Menu(12, palette), TitledFit("Factory") with
            {
                LoadingSkeleton = G9BottomSheetLoadingSkeleton.ListRows,
                LoadingSkeletonRowCount = 4
            });
            return Task.CompletedTask;
        }));

        stack.Add(Button("Factory that THROWS. EXPECT: an error popup — not a crash, not an eternal spinner", () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(
                () => throw new InvalidOperationException("Sheet Lab: this factory always fails."),
                TitledFit("Failing factory"));
            return Task.CompletedTask;
        }));

        stack.Add(Button("Awaitable show. EXPECT: a toast naming the handle once the sheet exists", async () =>
        {
            var handle = await G9BottomSheetHelper.ShowG9BottomSheetAsync(Menu(3, palette), TitledFit("Awaited"));
            await G9ToastHelper.ShowToastAsync(
                handle is null ? "Throttled — no sheet" : "Sheet attached, handle returned",
                G9ToastType.Information);
        }));

        stack.Add(Button("Stack three sheets quickly. EXPECT: each parent recedes, none is closed by the next", async () =>
        {
            G9BottomSheetHelper.ShowG9BottomSheet(Menu(7, palette), TitledFit("First"));
            await Task.Delay(450);
            G9BottomSheetHelper.ShowG9BottomSheet(Menu(4, palette), TitledFit("Second"));
            await Task.Delay(450);
            G9BottomSheetHelper.ShowG9BottomSheet(Menu(2, palette), TitledFit("Third"));
        }));

        return stack;
    }

    private void Apply(G9BottomSheetSettings settings)
    {
        _settings = settings;
        G9BottomSheetHelper.Configure(settings);

        if (_settingsLabel is not null)
        {
            _settingsLabel.Text =
                $"ENGINE — stage before show: {(settings.StageBeforeShow ? "ON" : "OFF (legacy)")} · "
                + $"settle frames: {settings.PreOpenSettleFrames} · max hold: {settings.PreOpenMaxHoldMs} ms · "
                + $"hardware layer: {(settings.UseHardwareLayerDuringMotion ? "on" : "off")} · "
                + $"motion: {(settings.MotionStyle == G9SheetMotionStyle.PlatformNative ? "platform-native" : "timed")} · "
                + $"frame clock: {(settings.UseFrameClockMotion ? "on" : "off")}";
        }
    }

    private static G9BottomSheetOptions TitledFit(string title) => G9BottomSheetOptions.FitToContentOptions() with
    {
        ShowToolbar = true,
        ShowCloseButton = true,
        Title = title,
        HeaderTitlePlacement = G9BottomSheetHeaderTitlePlacement.NearBack
    };

    private static List<G9BottomSheetListItem> Items(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new G9BottomSheetListItem { Key = i.ToString(System.Globalization.CultureInfo.InvariantCulture), Text = $"Item {i}" })
            .ToList();

    /// <summary>Rows with a leading glyph each — the shape of the app's operations menus.</summary>
    private static VerticalStackLayout Menu(int rows, G9Palette palette)
    {
        G9Glyph[] glyphs = [G9Glyph.Check, G9Glyph.Search, G9Glyph.Info, G9Glyph.Delete, G9Glyph.Close];
        var stack = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(16, 8, 16, 16) };

        for (var i = 0; i < rows; i++)
        {
            stack.Add(new G9NavCard
            {
                Title = $"Row {i + 1}",
                Subtitle = "Leading glyph, title, subtitle, chevron",
                Icon = glyphs[i % glyphs.Length],
                ShowChevron = true
            });
        }

        _ = palette;
        return stack;
    }

    private static View WrappingBody(G9Palette palette) => new VerticalStackLayout
    {
        Spacing = 10,
        Padding = new Thickness(16),
        Children =
        {
            new Label
            {
                Text = "Text that wraps is the case where a body's height depends on the width it is measured "
                     + "at. The sheet measures at its real width before it opens, so this paragraph must be "
                     + "fully visible above the footer on the very first frame — on a narrow phone and on a "
                     + "tablet alike, in both reading directions, at every font scale.",
                TextColor = palette.OnSurface,
                LineBreakMode = LineBreakMode.WordWrap
            },
            new Label
            {
                Text = "If the last line is cut off, or the sheet visibly grows after opening, the first measure "
                     + "is still running too early.",
                FontSize = 12,
                TextColor = palette.OnSurfaceVariant,
                LineBreakMode = LineBreakMode.WordWrap
            }
        }
    };

    private static View ResizableBody(G9Palette palette)
    {
        var rows = new VerticalStackLayout { Spacing = 8 };
        void AddRows(int count)
        {
            for (var i = 0; i < count; i++)
            {
                rows.Add(new G9NavCard { Title = $"Row {rows.Count + 1}", Icon = G9Glyph.Info });
            }
        }

        AddRows(2);

        return new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16),
            Children =
            {
                new Label
                {
                    Text = "Add and remove rows. The sheet must glide to its new height; the footer must never "
                         + "leave the screen edge, not even for a frame.",
                    FontSize = 12,
                    TextColor = palette.OnSurfaceVariant,
                    LineBreakMode = LineBreakMode.WordWrap
                },
                new G9Button
                {
                    Text = "Add 3 rows",
                    Variant = G9ButtonVariant.Outline,
                    Command = new Command(() => AddRows(3))
                },
                new G9Button
                {
                    Text = "Remove 3 rows",
                    Variant = G9ButtonVariant.Outline,
                    Command = new Command(() =>
                    {
                        for (var i = 0; i < 3 && rows.Count > 1; i++)
                        {
                            rows.RemoveAt(rows.Count - 1);
                        }
                    })
                },
                rows
            }
        };
    }

    private static G9Button FooterButton(string text, G9ButtonVariant variant) => new()
    {
        Text = text,
        Variant = variant,
        Command = new Command(() => G9BottomSheetHelper.CloseG9BottomSheet())
    };

    private static Label Section(string text, G9Palette palette) => new()
    {
        Text = text,
        FontSize = 14,
        FontAttributes = FontAttributes.Bold,
        TextColor = palette.OnSurface,
        Margin = new Thickness(0, 12, 0, 0)
    };

    private static Label Caption(string text, G9Palette palette) => new()
    {
        Text = text,
        FontSize = 12,
        TextColor = palette.OnSurfaceVariant,
        LineBreakMode = LineBreakMode.WordWrap
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
                // The lab must survive a broken sheet so the remaining cases stay testable.
                await G9ToastHelper.ShowToastAsync(ex.Message, G9ToastType.Error);
            }
        })
    };
}
