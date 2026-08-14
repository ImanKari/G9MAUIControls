# 06 - Android Debug Loop

# G9MAUIControls + G9Controls.Gallery
## Exercising the gallery on a device or emulator over `adb`, without a debugger

> ⚠ **Do not start a device session on your own initiative.** Deploying, launching and driving an app
> belongs to an explicit request. Reading a guide is not a reason to run it.

Adapted from a sibling project's loop and re-grounded on this repository: two apps, no ArcGIS, no NFC, no
Shell tab bar, and a control suite whose failure modes are its own. The universal mechanics were kept
because they were paid for elsewhere; everything project-specific was replaced.

There is no Visual Studio debugger and no Hot Reload here. Every iteration is
**edit → build → deploy → exercise → observe → decide**.

---

# 0. Read the death reason BEFORE the logs

`dumpsys activity exit-info <package>` is the first command of any crash investigation, not the last.
Android keeps a ring buffer of recent process deaths with a **decoded reason**, and it works for a death
that happened before you started watching — including one from yesterday.

```powershell
& $adb -s emulator-5554 shell "dumpsys activity exit-info com.byteorbit.g9nodecontrol"
```

The `reason=` field tells you which buffer to go read, which is the whole point:

| `reason=` | What it means | Where the evidence is |
|---|---|---|
| `4 (APP CRASH(EXCEPTION))` | a managed or Java exception | `logcat -b crash` — you get a full stack |
| `5 (CRASH_NATIVE)` | SIGSEGV/SIGABRT — AOT, Skia, interop | `/data/tombstones/`, `libc`/`DEBUG` tags |
| `6 (ANR)` | UI thread blocked | `/data/anr/traces.txt` |
| `3 (LOW_MEMORY)` / `lmkd` | the SYSTEM reclaimed it — **not a crash** | `logcat -b events` |
| `10 (USER REQUESTED)` / `REMOVE TASK` | somebody swiped it away | nothing to fix |

**Why this ordering matters.** A native crash and a low-memory kill both look identical from inside the
app — the app writes nothing on the way out, because it never runs code on the way out. Guessing from
silence costs an afternoon; `exit-info` costs two seconds.

## The corollary: a silent app log is evidence, not an absence of it

If the app's own logging says nothing about a death, that **rules out** the managed exception path and
points at the four rows below the first. Do not add more app-side logging to chase it; look outside the
process.

---

# 1. Toolchain

## 1.1 adb is not on PATH

```powershell
$adb = "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"
```

## 1.2 Pin one device — this workstation has two attached

```powershell
& $adb devices -l
# RF8N82L0TGL     device  model:SM_A217F              ← physical Galaxy A21s (Android 10-class, arm)
# emulator-5554   device  model:sdk_gphone64_x86_64   ← Pixel 9 Pro XL AVD, API 36, x86_64
```

**Always pass `-s <serial>`.** With two devices attached, an unqualified `adb shell` fails with
`more than one device/emulator`, and an unqualified `dotnet build -t:Run` picks one arbitrarily — so you
can deploy to the phone while reading logs from the emulator and conclude the build did nothing. Pass
the target through to the build too:

```powershell
"-p:AdbTarget=-s emulator-5554"
```

The two devices are worth keeping around: the emulator is x86_64 API 36, the phone is arm and several
API levels older. They fail differently, and the suite's platform handlers (`G9ShimmerBandView`,
`G9SheetViewBorder`) are exactly the code where that matters.

## 1.3 Packages

| App | Package |
|---|---|
| `G9Controls.Gallery` | `com.byteorbit.g9controls.gallery` |

```powershell
& $adb -s emulator-5554 shell "pm list packages | grep -i -E 'byteorbit|g9'"
```

## 1.4 Discover the activity — never hardcode it

```powershell
& $adb -s emulator-5554 shell "dumpsys package com.byteorbit.g9nodecontrol" 2>&1 | Select-String "Activity"
```

The `crc64…` class name is generated and **changes between builds**. Hardcoding it produces a launch
that silently starts nothing.

## 1.5 Build, deploy, launch

```powershell
dotnet build MAUI\G9Controls.Gallery\G9Controls.Gallery.csproj -t:Run `
    -f net10.0-android -c Debug --no-restore "-p:AdbTarget=-s emulator-5554" -v:m
```

- `-t:Run` builds, installs and launches. `-t:Install` redeploys without launching.
- `-t:Install --no-build` installs what is already built.
- `--no-restore` saves a few seconds; drop it when packages changed.
- **This solution's builds are slow** — the core is ~51k LOC across four TFMs. Constrain with `-f` always.

## 1.6 Logs — always `-d`, never streaming

```powershell
& $adb -s emulator-5554 logcat -G 64M          # once per boot: raise the ring buffer
& $adb -s emulator-5554 logcat -c              # clear
# ... launch and exercise ...
& $adb -s emulator-5554 logcat -b crash -d -v threadtime
& $adb -s emulator-5554 logcat -d -v threadtime AndroidRuntime:E DOTNET:V MonoDroid:V *:S
```

**`-G 64M` first.** The default buffer wraps in seconds under load, so on a rare crash the reason has
already scrolled out of the *device's* memory before you notice the app is gone.

**Never leave logcat streaming from an agent loop** — it never returns and the loop freezes. `-d` dumps
and exits.

## 1.7 Screenshot and UI tree

```powershell
& $adb -s emulator-5554 exec-out screencap -p > screen.png

& $adb -s emulator-5554 shell uiautomator dump /sdcard/ui.xml
& $adb -s emulator-5554 pull /sdcard/ui.xml ui-dump.xml
```

Two **separate** calls — the pull sometimes needs a moment after the dump. Extract text:

```powershell
Select-String -Path ui-dump.xml -Pattern 'text="[^"]+' -AllMatches |
    ForEach-Object { $_.Matches.Value }
```

Bounds for a specific label, so taps are never guessed:

```powershell
$c = Get-Content ui-dump.xml -Raw
$m = [regex]::Match($c, 'text="Relays"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"')
$x = ([int]$m.Groups[1].Value + [int]$m.Groups[3].Value) / 2
$y = ([int]$m.Groups[2].Value + [int]$m.Groups[4].Value) / 2
& $adb -s emulator-5554 shell input tap $x $y
```

## 1.8 Reset

```powershell
& $adb -s emulator-5554 shell am force-stop com.byteorbit.g9nodecontrol
& $adb -s emulator-5554 shell pm clear   com.byteorbit.g9nodecontrol   # wipes SecureStorage + the SQLite db
```

`pm clear` wipes app data, which for this suite means the persisted theme choice and any
`G9BottomSheetHeightSeeds` measurements the app had learned. That is usually what you want when
reproducing a first-launch bug, and misleading when you are not — a fit-to-content sheet will settle on
its first open again.

---

# 2. Timeout discipline

An agent loop that blocks is an agent loop that is over.

| Command class | Timeout |
|---|---|
| `dumpsys`, `uiautomator`, `pull`, `screencap` | 5–8 s |
| `dotnet build` for one TFM | 180–600 s |
| streaming logcat | **never** — use `-d` |

One adb call per execution block. Chaining several in one shell invocation is how a hang becomes
untraceable.

---

# 3. Instrumentation, only when the stack is not enough

Most failures here need none: a managed crash gives a full stack from the crash buffer. Add tracing only
after `exit-info` and the crash buffer have been read and were insufficient.

If you do add it, **use `Android.Util.Log.Info`, not `.Debug`**. Android drops DEBUG priority for a tag
unless the tag is explicitly enabled (`setprop log.tag.<TAG> VERBOSE`), and stock builds default to INFO.
A logger that silently emits nothing is worse than no logger — it reads as "the code never ran".

`Console.WriteLine` is unreliable on Android MAUI. Mark every temporary line `// AGENT_TRACE`, and before
declaring done:

```powershell
rg -n "AGENT_TRACE" MAUI --glob "*.cs" --glob "*.xaml"   # must return zero
```

---

# 4. Failure modes specific to THIS suite

Each of these is a real defect that was hit, with the signature that identifies it.

## 4.1 `NullReferenceException` in `G9Theme.ApplyCurrent` at startup

```
[System.NullReferenceException]
  at G9MAUIControls.Theming.G9Theme.ApplyCurrent
  at G9MAUIControls.Theming.G9Theme.Init
  at <YourApp>.MauiProgram.CreateMauiApp
  at Microsoft.Maui.MauiApplication.OnCreate
```

**`G9Theme.Init()` was called from `CreateMauiApp`, where `Application.Current` is still null.** It must
be called from the `App` constructor, after `InitializeComponent()`:

```csharp
public App()
{
    InitializeComponent();   // merges G9PageTemplate + the theme dictionary
    G9Theme.Init();          // needs Application.Current, and needs the dictionaries already merged
}
```

Init both applies the palette and subscribes to `RequestedThemeChanged`, so there is no Application to
attach to and nowhere to push ~110 palette tokens before one exists. The core now throws a named
`InvalidOperationException` here instead of an NRE — if you see the old NRE, the package is stale.

## 4.2 A page throws "G9PageTemplate not found in resources"

`App.xaml` did not merge the template, or merged it by `Source=` path. A path resolves only inside the
declaring assembly, so from a consumer it fails — **merge by type**:

```xml
xmlns:g9Hosting="clr-namespace:G9MAUIControls.Hosting;assembly=G9MAUIControls"
...
<g9Hosting:G9PageTemplate />
```

LES-0013. Note this one fails at XAML *compile* time in Release and at *runtime* in Debug, because Debug
does not compile XAML (LES-0002).

## 4.3 Square corners and collapsed margins everywhere

`G9Theme.Init()` never ran. It seeds the three `DynamicResource` layout keys, and `DynamicResource` has
no fallback — an absent key silently leaves the property unset, with nothing in the build output.

## 4.4 Build fails with `MCTME001`

Any app referencing `G9MAUIControls.IntroCarousel` must chain
`.UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)`. The analyzer fails the
build, not the run.

## 4.5 `NETSDK1144` on a trimmed publish

The SQLite package maps by reflection and declares it (ADR-0014). A consumer needs **both**
`TrimmerRootAssembly` and `WarningsNotAsErrors` for `IL2026;IL2070;IL2077;IL2087;IL2091;IL2111` — the
package's own relaxation does not travel. LES-0015.

## 4.6 Keyboard does not dismiss on tapping outside; safe-area wrong after rotation

`MainActivity` is missing the `G9AndroidHost` hooks. All four are required and none of them crash when
absent — see `G9Controls.Gallery/Platforms/Android/MainActivity.cs`, which exists as the reference
implementation.

## 4.7 Assemblies not loading after a hand-installed Debug APK

```
monodroid-assembly: open_from_bundles: failed to load bundled assembly …
```

A Debug build is a shell APK plus assemblies pushed separately to
`/data/user/0/<pkg>/files/.__override__/<abi>/`. `adb install -r` of that APK leaves the OLD assemblies
in place, so the process keeps running the previous build while the APK checksum matches the new one
perfectly. Deploy with `-t:Install`, or embed:

```powershell
dotnet build … -p:EmbedAssembliesIntoApk=true -p:AndroidFastDeploymentType=Assemblies
```

## 4.8 A package installed by `adb install` sits in Android's stopped state

Until it is launched by hand once, the system will not start it from an intent. Launch it manually after
every raw install before concluding anything from a silent trigger.

---

# 5. What to look at, per app

## `G9Controls.Gallery` — the visual pass

This is what the app exists for, and it is the project's largest open gap. `09-Progress.md` →
"The honest gap" is the authoritative checklist. In device terms:

| Page | Look for |
|---|---|
| **Glyphs** | off-centre geometry, stroke weight drifting between neighbours, a glyph that reads at 24 and not at 14, anything that vanishes on the accent fill |
| **Inputs** | the **disabled** rows on Android — a clipped floating label is the §15 A5 regression |
| **Actions** | no two variants alike; Error/Warning must not read as Primary. Press each control at its **edge** |
| **Overlays** | popup above sheet, toast above both, toast outliving the sheet that raised it |
| **Navigation** | the FAB notch: a smooth concave cut-out, no hard edge, no clipped halo, **both themes** |
| **Satellites** | the barcode field must be indistinguishable from the core field beside it |

Then flip the **Theme** and **RTL** toolbar toggles and walk all six again. In RTL, icon slots must swap
columns and **no glyph may mirror** — a backwards tick is the §9 double-flip bug.

---

# 6. The loop

1. **Hypothesis** — one sentence: "X happens because Y."
2. **Read** `exit-info`, then the crash buffer. Do not edit code before a log supports the hypothesis.
3. **Instrument** only if the stack was insufficient.
4. **Build and deploy** one TFM.
5. **Clear logcat, exercise, dump with `-d`.**
6. **Decide** — confirmed → fix; not confirmed → new hypothesis *from the evidence*, not from a guess.
7. **Confirm twice from a clean state** (`force-stop`, relaunch) before calling it fixed.

---

# 7. Anti-patterns

- Editing code before reading a log that proves the hypothesis.
- Running `*:V` and claiming you read it.
- Tapping hardcoded coordinates without a fresh `uiautomator` dump.
- `Thread.Sleep` to wait for UI — poll the dump instead.
- Streaming logcat from an agent loop.
- Chaining adb calls in one invocation.
- Omitting `-s <serial>` on a workstation with two devices attached.
- Declaring a fix good after one run.
- Concluding "no crash log" means "no crash" — see §0.

---

# 8. Output format

While iterating:

```
MODE: DEBUG | DESIGN
ITERATION: N
HYPOTHESIS: <one sentence>
COMMANDS: <copy-pasteable>
EVIDENCE: <the log excerpt that decided it>
DECISION: <conclusion; what next>
```

When done:

```
FIXED: <what, and where the boundary was>
EVIDENCE: <the proof — a stack that is gone, a screenshot, a pid that survived>
CLEANUP: AGENT_TRACE count zero, screenshots removed
RESIDUAL RISK: <what is still untested>
```
