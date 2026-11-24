# AGENTS.md

Portable Windows taskbar monitor: one WPF app in `TaskbarMonitor/`; the shipped
build is the single-file `dist/TaskbarMonitor.exe` (~150 MB, self-contained).

Git repo on `main`, remote `origin` = `AnwerAbdelkadir/Taskbar-Monitor`. Verified
on Windows 11 (26200) and Windows 10 22H2 (19045); multi-monitor is exercised on
three displays, vertical taskbars are implemented but untested.

## Repo gotchas

- The root `README.md` is the GitHub face of the project and the canonical feature
  list: refresh it after every feature change, and re-capture the `assets/`
  screenshots (taskbar strip, settings window, graphs window) when the UI changes.
  All three are plain desktop captures of the window rects (`CopyFromScreen`), which
  also works on the elevated instance; the graphs window needs an unelevated copy,
  because it only opens on a left-click. `dist/README.md` is the end-user copy of
  `TaskbarMonitor/README.md`, copied by hand on publish — not of the root README.
- The root `.gitignore` ignores only `dist/TaskbarMonitor.exe`, so publishing adds
  nothing to `git status`. `dist/README.md` **is** tracked and must be refreshed by
  hand after every publish.
- The old WinRing0 driver `dist/TaskbarMonitor.sys` is deleted since `e25afcc` but
  still lives in older commits. Never restore it — a checkout or revert of an old
  revision would bring back the Defender-flagged driver.
- The first commit also contains OpenCode session dumps (`11111111111`,
  `ses_*.json`); they are history only now. Do not commit such exports.

## Build, run, ship

- .NET SDK 8.0.428 lives in `%USERPROFILE%\.dotnet\dotnet.exe`. The `dotnet` on
  PATH is the runtime-only host (`C:\Program Files\dotnet` has no `sdk\`), so
  invoke the SDK by full path.
- Build: `dotnet build TaskbarMonitor/TaskbarMonitor.csproj -c Debug`
- Run: `TaskbarMonitor/bin/Debug/net8.0-windows/TaskbarMonitor.exe`
  (no args = settings window, `--silent` = overlay only, `--replace` = take over
  from a running copy).
- Ship after changes:

  ```
  dotnet publish TaskbarMonitor/TaskbarMonitor.csproj -c Release -r win-x64 \
    --self-contained true -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
    -p:DebugSymbols=false -o dist
  ```

  Then copy `TaskbarMonitor/README.md` to `dist/README.md`; that copy is manual,
  not part of publish.
- Release after changes (the user asked the agent to run the whole chain). Bump
  `<Version>` in `TaskbarMonitor.csproj` (features → minor, fixes → patch) — it also
  drives the settings footer (`AppVersion.cs`) and the exe file version — build and
  publish as above, then commit, tag `v<version>`, push `main` + tags, and attach
  the exe to a GitHub release:

  ```
  gh release create v1.1.0 dist\TaskbarMonitor.exe -t "v1.1.0" -n "<notes>"
  ```

  `gh` is not on PATH; run `%LOCALAPPDATA%\GitHubCLI\extract\bin\gh.exe`. The repo
  is public and git credentials live in Windows Credential Manager, so push works
  without re-auth. The README `shields.io/github/v/release` badge follows the latest
  release automatically.
- Debug and `dist` copies share `%AppData%\TaskbarMonitor` (settings + log), so
  switching between them keeps configuration. The running copy locks its own exe,
  so a build or a publish over it fails.

## Verification (no tests exist)

No test project, no CI. Verify by building, launching, and watching the real
taskbar (`MetricsService.Latest` holds the live snapshot). Elevation is the main
obstacle: UIPI blocks UI Automation **and** synthetic clicks/keystrokes from a
normal-integrity process into the running elevated instance, so drive the UI on an
unelevated Debug run. Open the settings window of the running instance with
`[System.Threading.EventWaitHandle]::OpenExisting("Local\TaskbarMonitor.ShowSettings").Set()`.
Edit `%AppData%\TaskbarMonitor\settings.json` **only while the app is closed**:
`App.ExitApp` saves the in-memory settings over the file, wiping external edits.
Practical automation: screenshot the taskbar, read
`%AppData%\TaskbarMonitor\settings.json`, drive WPF ComboBoxes with
`SetFocus` + `SendKeys` (`{F4}`, type the item, `{ENTER}`), and set the Position
boxes with `ValuePattern.SetValue`. Synthetic clicks do not reach native MessageBox
buttons; post `WM_COMMAND` with `IDNO` (7) instead. Anything touching CPU
temperature or frequency has to be checked in an elevated instance — both come
from the same LHM/PawnIO read.

## Rebuilding while it runs

The running exe locks `bin/.../TaskbarMonitor.exe`, so the build fails. Close it
gracefully first — this also works for elevated copies, where `Stop-Process` is
denied:

```powershell
[System.Threading.EventWaitHandle]::OpenExisting("Local\TaskbarMonitor.Exit").Set()
```

## Toolchain quirks that break the build

- `UseWindowsForms=true` is only for `Screen` (monitor list); the csproj removes
  the implicit `System.Drawing` and `System.Windows.Forms` usings. Use
  `using WinForms = System.Windows.Forms;` and never import them unaliased.
- `System.IO` is a global using, so `Path` resolves to `System.IO.Path`. In UI
  files alias `using Path = System.Windows.Shapes.Path;`.
- The `.cs` files are UTF-8 **without BOM**. Never rewrite them with
  `Get-Content`/`Set-Content` in Windows PowerShell 5.1 — it reads them as ANSI
  and corrupts `↑ ↓ ° · —` (turned into `â†'`, `Â°`). Use the edit tool, or
  `[IO.File]::ReadAllText/WriteAllText` with an explicit UTF8 encoding.
- x64 only (`GetWindowLongPtrW`, NVML); `app.manifest` is `asInvoker` with
  PerMonitorV2 DPI, so elevation is opt-in at runtime, never through the manifest.

## Architecture notes

- `App.xaml.cs`: single instance (`Local\TaskbarMonitor.Instance` plus Show/Exit
  events), the metrics timer and the overlay list. Static API the UI calls:
  `Settings`, `ApplySettings`, `RebuildOverlays`, `QueueSave`, `Latest`.
  Settings and log live in `%AppData%\TaskbarMonitor`.
- `OverlayWindow` is a transparent topmost window positioned in physical pixels
  over `Shell_TrayWnd` (and `Shell_SecondaryTrayWnd`), left of the tray. The tray
  edge comes from `TrayNotifyWnd` on Windows 10; Windows 11 leaves that window
  0×0, so the leftmost `SystemTray.*` UI Automation button (chevron, clock) is
  used instead, cached for 5 s per taskbar handle.
  It caches the taskbar handle and polls every 150 ms; do not add per-tick
  `UpdateLayout()`. `RedrawWindow` on move/resize/close prevents ghost pixels.
  Placement is clamped inside the taskbar and left of the tray, and the tray gap
  applies to manual offsets too.
  A dedicated 25 ms guard re-asserts `HWND_TOPMOST` (the placement tick stays at
  150 ms): the shell raises `Shell_TrayWnd` above topmost windows for the ~100 ms
  its button animations run — window minimize/restore, thumbnails, flyouts — and a
  covered overlay never recovers by itself otherwise. This shows up on the primary
  monitor only, because the shell does not reorder `Shell_SecondaryTrayWnd`.
- `ShowInFullscreen` (default off) keeps the overlay visible while the shell has
  hidden the taskbar for a full-screen program. `Fullscreen.Refresh` runs on a 1 s
  timer and uses `SHQueryUserNotificationState` (`QUNS_BUSY` /
  `QUNS_RUNNING_D3D_FULL_SCREEN`) with a foreground-window fallback that ignores
  captioned, desktop and shell windows. The overlay then reuses the last seen
  taskbar rect (`_lastBar`/`_lastTrayLeft`), or `HiddenBar()` when it started
  during full-screen.
- The Network widget measures the adapter in `AppSettings.NetAdapter` (empty = every
  active adapter, filtered against loopback, tunnels and the usual virtual NIC
  descriptions). An explicit pick bypasses the filters, so a VPN or virtual NIC can
  be watched on purpose; the picker lives in the Network widget row.
- One Disk widget, one Drive picker. `Normalize` folds any extra Disk blocks
  (written by an earlier multi-disk build) into the first. The plumbing below
  still has a per-drive shape on purpose: the snapshot carries a `Disks`
  dictionary (resolved drive → activity/space) and `MetricsService` owns one
  `DiskSampler` per drive, because the graphs and alerts sit on top of it; with
  a single widget it holds exactly one entry. `MeterView.PlanColumns` works on
  blocks (not kinds) and would expand several Disk blocks if they ever existed.
- The overlay panel's fill is `MeterView.HitTestFill` (black at alpha 1), not
  `Transparent`: a layered window with zero alpha is click-through, so
  right-click would stop working between the readings.
- Position uses device pixels: logical settings × `GetDpiForWindow` at placement
  time. Sliders are paired with numeric boxes via `BindNumber` (the slider owns
  the value); ranges are 0–80 gap, 0–900 right offset, −30..40 vertical, matching
  `AppSettings.Normalize`.
- `MeterView` renders two rows from the fixed `AppSettings.LayoutOrder()`:
  Network spans both rows (upload over download), then CPU/GPU, RAM/Disk and
  Battery. A disabled reading keeps its slot; a fully empty column is dropped.
  `Stabilize()` raises the panel's `MinWidth` to the widest content seen since
  settings changed: the block is right-anchored, so without it every digit-width
  change shoves the whole thing sideways. It never shrinks on its own — only
  `Apply` resets it. Do not right-align or reserve per-widget slots instead; that
  was tried and looked wrong (480 px block, gaps between values).
  Visibility lives only in the Widgets switches. The percentage is always the
  primary value (and the warning-color source); `ShowAmount` gates the RAM/Disk
  used/total extra, `ShowVram` the GPU memory, `ShowClock` the CPU frequency
  (LHM, admin-gated) and `ShowFan` the GPU fan (NVML percentage, or rpm from the
  LHM fallback). Warning colors come from
  `WarnPercent`/`CriticalPercent`/`WarnTempC`/`CriticalTempC` (clamped in
  `AppSettings.Normalize`), set from Appearance → Readings/Warnings.
- Widget names are fixed: `MetricKind.DefaultLabel()` (Network has none). There
  is no custom label; do not reintroduce a `Label` setting.
- `MetricKind.Vram`, `DiskActivity`, `DiskSpace`, `NetUp`, `NetDown` are legacy
  values kept only so old settings load; `AppSettings.Normalize` folds them into
  Gpu, Disk and Network.
- Battery is force-disabled at startup when `BatterySampler.IsPresent()` is
  false, and its switch is disabled on desktops ("No battery detected").
- The UI label is **Widgets**, but code and XAML still say `Blocks`
  (`Tag="Blocks"`, `BlocksPage`, `BlocksPanel`, `BlockSettings`,
  `RebuildBlocks`). Rename user-facing strings only.
- `SettingsWindow.xaml` control names are referenced directly from the
  code-behind; widget rows, the inline-expanded options and the `OptionChip`
  switches are all built in code (`BuildBlockRow`, `BuildOptions`, `OptionChip`),
  driven by `_selectedBlock`.
- Settings-window colors must be `DynamicResource` (styles can stay
  `StaticResource`): `Surface`, `Ink`, `Muted`, `LineSoft`, `Accent`, `Chip`,
  `RowHighlight`, `CardBg`, `CardBorder`, `FieldBorder`, `SectionFg`, `BadgeBg`,
  `BadgeInk`, `SwitchTrack`, `SliderTrack`, `HoverBg`, `PressedBg`. Any new
  color needs a key in `SettingsWindow.xaml` and an entry in
  `Palette.AppColors()` for both themes, or dark mode shows a stale color.
  Styles must theme `Foreground` as well as backgrounds: `SecondaryButton` shipped
  without it and "Reset defaults" was invisible in dark mode until it got
  `Foreground="{DynamicResource Ink}"`.
  Exception: the preview strip and its clock are set in code from
  `Palette.PreviewBarBackground()`/`palette.Value`, because that mock follows the
  taskbar palette (`Palette.Create`), not the window theme. Do not hard-code it.
- `History` (in `MetricsService.cs`) is a 120-sample ring buffer every tick appends
  to. `GraphsWindow.cs` draws it as sparklines on a 1 s timer and is opened by
  left-clicking the overlay (`App.ShowGraphs`, single instance); its chrome stays
  dark in both themes, like the settings sidebar. It only draws while open.
- `Alerts.cs` evaluates every snapshot on the UI thread (`App`'s Metrics.Updated
  handler): a value above its critical threshold for a full minute raises one
  borderless popup near the primary tray plus `SystemSounds.Exclamation`, then
  stays suppressed until the value recovers. `AppSettings.AlertsEnabled` gates it
  (Appearance → Alerts). Thresholds cannot be lowered below sane floors
  (Normalize clamps them), so testing needs real load, not settings tricks.
- Settings apply live and auto-save (400 ms debounce via `QueueSave`).
  `Settings.Theme` styles only the settings window: `ApplyChrome()` copies
  `Palette.AppColors(dark)` into those keys (sidebar and dark title bar stay
  fixed; Win10 cannot flip the caption back to light at runtime). The overlay
  ignores the theme and always follows `SystemUsesLightTheme`;
  `palette.Label/Value/…` only color the text, and there are no custom color
  settings.
- Sensors: CPU temperature and frequency need administrator **and the PawnIO
  driver installed** (LibreHardwareMonitor 0.9.6 uses PawnIO; WinRing0 is gone, so
  no `.sys` is written next to the exe). Both are LHM sensors on the same `Computer`,
  but `SampleTemperature`/`SampleClock` each run their own `_cpu.Update()`, so keep
  further LHM reads off the per-tick path. GPU usage, VRAM, temperature and fan come
  from NVIDIA NVML; when NVML is missing (Intel / AMD) `LhmGpu` reads the first GPU
  through LibreHardwareMonitor on its own `Computer`, and the fan arrives as
  `GpuFanRpm` (shown `Fan 1200RPM`). Only exercised through
  `TASKBARMONITOR_GPU_SOURCE=lhm` on the NVIDIA box — untested on real Intel/AMD
  hardware. Unavailable values are `NaN` and are omitted rather than zeroed.
  Optimus GPUs error while asleep, so 0 % GPU is a deliberate fallback; `Fan 0%` is a
  real reading because desktop cards stop their fans at idle. There is no power (W)
  reading: `ShowPower` and the CPU/NVML power samples were removed.
- Admin-gated options: turning on CPU Temperature or Frequency while not elevated
  prompts a restart (`SetAdminOption` → `App.RestartElevated`), which relaunches
  with `--replace` and reopens the settings window. Declining flips the switch
  back off. GPU temperature/VRAM/fan come from NVML (or the LHM fallback) and are
  never gated. The expanded CPU/GPU widget row carries the hint that explains the
  empty reading: `BuildSensorHint`/`UpdateSensorHint` (refreshed by the 2 s status
  timer) show **Restart as administrator** and/or **Install the PawnIO driver** —
  the latter downloads `PawnIO_setup.exe` from the release and runs it `-install`
  elevated, then offers the restart. It is hidden on first open: the locked chips
  (`OptionChip` with a `LockReason`) stay dimmed and un-toggleable, and a click on
  one only reveals the hint (`_hintRevealed`), so the buttons appear when the user
  reaches for Temperature/Frequency/VRAM/Fan, not before. `PawnIoInstalled()` needs
  the `PawnIO` service key **and** `%ProgramFiles%\PawnIO\PawnIOLib.dll` (the
  official uninstaller leaves the key behind); the GPU hint needs
  `Metrics.GpuUsesLhm`, because NVIDIA reads through NVML. GPU `--` on Intel/AMD is
  the same missing driver. `DefaultBlocks` starts the CPU block with
  `ShowTemperature`/`ShowClock` off; GPU temperature stays on (NVML) — do not put
  the install UI back on Startup.
- Startup is the HKCU Run key, or a scheduled task with `/RL HIGHEST` when
  elevated startup is enabled. The shipped setup uses the task (`StartWithWindows`
  + `StartElevated`) so CPU temperature works from every logon with no UAC prompt.
  Toggling either Startup box calls `Startup.Apply`, which **imports** an XML
  definition instead of `schtasks /Create`: the command line version sets "do not
  start on batteries" and a 72-hour run limit, which would block laptops and kill
  the overlay after three days.
- Auto-update: `UpdateService` reads the latest GitHub release (`releases/latest`,
  asset **must** stay named `TaskbarMonitor.exe`) and `UpdateWindow` is the single
  progress surface (taskbar menu, Startup page, automatic startup check). A running
  exe can be renamed but not overwritten, so `InstallAsync` downloads
  `TaskbarMonitor.exe.new`, verifies the API's sha256 digest, moves the old exe to
  `.old`, moves the new one into place and starts it with `--replace`; the next
  start deletes `.old`. The daily check rides on the existing 2 s `_themeTimer` in
  `App` and only pops the window when the app was not started with `--silent`.

## Sensor setup on a fresh machine

Temperatures silently stay hidden (no error, no `.sys`) until PawnIO is present.
Install the signed setup from <https://github.com/namazso/PawnIO.Setup/releases>
with `PawnIO_setup.exe -install` elevated (`/S` exits 87 and installs nothing),
then run the app elevated. If a stray `TaskbarMonitor.sys` reappears, an old build
or the leftover `R0TaskbarMonitor` driver service is still registered.
