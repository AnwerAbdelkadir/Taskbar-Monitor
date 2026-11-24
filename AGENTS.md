# AGENTS.md

Portable Windows taskbar monitor: one WPF app in `TaskbarMonitor/`, shipped as the
single-file `dist/TaskbarMonitor.exe` (~150 MB, self-contained, x64 only). Git
`main`, remote `AnwerAbdelkadir/Taskbar-Monitor`. Verified on Windows 11 (26200) and
Windows 10 22H2 (19045); multi-monitor is exercised on three displays, vertical
taskbars are implemented but untested. No test project, no CI.

## Commands

- The .NET SDK lives in `C:\Users\Anwer\.dotnet` (8.0.428). `C:\Program Files\dotnet`
  has the runtime only, so plain `dotnet build` fails with "No .NET SDKs were found"
  — call `& "$env:USERPROFILE\.dotnet\dotnet.exe"` explicitly.
- Build: `& "$env:USERPROFILE\.dotnet\dotnet.exe" build TaskbarMonitor/TaskbarMonitor.csproj -c Debug`
- Run: `TaskbarMonitor/bin/Debug/net8.0-windows/TaskbarMonitor.exe`
  (no args = settings window, `--silent` = overlay only, `--replace` = take over
  from a running copy).
- Publish:

  ```
  & "$env:USERPROFILE\.dotnet\dotnet.exe" publish TaskbarMonitor/TaskbarMonitor.csproj `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
    -p:DebugSymbols=false -o dist
  ```

  Then copy `TaskbarMonitor/README.md` to `dist/README.md` by hand — publish does
  not do it, and the copy is tracked.
- Deploy to the copy this machine actually runs: the logon task `TaskbarMonitor`
  runs `"D:\Sources\Others\TaskbarMonitor.exe" --silent` elevated. Stop it first
  (Exit event below), `Copy-Item dist\TaskbarMonitor.exe D:\Sources\Others\`, then
  `Start-ScheduledTask -TaskName TaskbarMonitor` — no UAC prompt, and the app comes
  back elevated with sensors working.
- Release: bump `<Version>` (features → minor, fixes → patch; it drives the settings
  footer via `AppVersion.cs` and the exe file version), publish, copy the README,
  commit, tag `v<version>`, `git push origin main` and the tag, then create the
  release. `gh` is installed but not logged in ("not logged into any GitHub hosts"),
  so use the REST API with the PAT git already stores:

  ```powershell
  $token = (("protocol=https`nhost=github.com`n`n" | git credential fill |
      Where-Object { $_ -like 'password=*' }) -replace '^password=', '')
  # POST https://api.github.com/repos/.../releases  (tag_name/name/body)
  # then POST <upload_url split on '{'>?name=TaskbarMonitor.exe
  #   -ContentType application/octet-stream -InFile dist\TaskbarMonitor.exe -TimeoutSec 900
  ```

  `upload_url` carries a `{?name,label}` template, strip it by splitting on `{`.
  GitHub returns the asset `digest`; compare it with `Get-FileHash` — the in-app
  updater verifies it. The README `shields.io/.../v/release` badge and
  `releases/latest` follow automatically.
- Stop a running copy (this also works on the elevated one, where `Stop-Process` is
  denied): `[System.Threading.EventWaitHandle]::OpenExisting("Local\TaskbarMonitor.Exit").Set()`.
  `Local\TaskbarMonitor.ShowSettings` opens its settings window instead.
- Debug and deployed builds share `%AppData%\TaskbarMonitor` (settings + log). A
  running copy locks its own exe, so building or copying over it fails.

## Verify without tests

- Drive the **unelevated Debug build**: UIPI blocks UI Automation and synthetic
  clicks/keystrokes from a normal-integrity process into the elevated instance.
- Edit `%AppData%\TaskbarMonitor\settings.json` only while the app is closed —
  `App.ExitApp` writes the in-memory settings over the file.
- What works for placement/visibility checks (PowerShell + `Add-Type`):
  - `Graphics.CopyFromScreen` of a band around the bar (primary bar was
    `0,1392–2560,1440` on a 2560×1440 primary) shows the real pixels.
  - the overlays are top-level `HwndWrapper[TaskbarMonitor;…]` windows; `EnumWindows`
    over the app's pid plus `GetWindowRect`/`IsWindowVisible` separates "hidden by
    the app" from "covered by the shell".
  - `keybd_event` for Win / Win+A / Win+S / Esc, `mouse_event` for a desktop click,
    `WindowFromPoint` to learn which window really owns a pixel when z-order is the
    question.
  - CPU temperature and frequency only exist in an elevated instance (LHM/PawnIO).
- Native MessageBox buttons ignore synthetic clicks; post `WM_COMMAND` with `IDNO`
  (7) instead.

## Repo gotchas

- The root `README.md` is the GitHub face and the canonical feature list: refresh it
  after every feature change and re-capture `assets/` (taskbar strip, settings,
  graphs) when the UI changes. They are plain `CopyFromScreen` captures of the
  window rects; the graphs window needs an unelevated copy because it only opens on
  a left-click.
- `.gitignore` ignores only `dist/TaskbarMonitor.exe`, so publishing adds nothing to
  `git status`. `dist/README.md` **is** tracked.
- The WinRing0 driver `dist/TaskbarMonitor.sys` was deleted in `e25afcc` but lives in
  older commits — never restore it (Defender-flagged).
- The first commit holds OpenCode session dumps (`11111111111`, `ses_*.json`); they
  are history only. Do not commit such exports.
- Sources are UTF-8 **without BOM** and CRLF. Never rewrite them with
  `Get-Content`/`Set-Content` in Windows PowerShell 5.1 — it reads ANSI and corrupts
  `↑ ↓ ° · —` (into `â†'`, `Â°`). Use the edit tool, or
  `[IO.File]::ReadAllText/WriteAllText` with an explicit UTF8 encoding. Files created
  with the write tool arrive LF-only; convert them so the diff stays clean.

## Architecture notes

- `App.xaml.cs`: single instance (`Local\TaskbarMonitor.Instance` plus the Show/Exit
  events), the metrics timer and the overlay list. Static API the UI uses:
  `Settings`, `ApplySettings`, `RebuildOverlays`, `QueueSave`, `Latest`. Settings and
  log live in `%AppData%\TaskbarMonitor`.
- `OverlayWindow` is a transparent topmost window placed in physical pixels over
  `Shell_TrayWnd` (and `Shell_SecondaryTrayWnd`), left of the tray. The tray edge
  comes from `TrayNotifyWnd` on Windows 10; Windows 11 leaves it 0×0, so the leftmost
  `SystemTray.*` UI Automation button is used, cached 5 s per taskbar handle. It
  caches the taskbar handle and polls every 150 ms; do not add per-tick
  `UpdateLayout()`. `RedrawWindow` on move/resize/close prevents ghost pixels.
  Placement is clamped inside the bar and left of the tray; the tray gap applies to
  manual offsets too.
- A dedicated 25 ms guard re-asserts `HWND_TOPMOST` (placement stays at 150 ms): the
  shell raises `Shell_TrayWnd` above topmost windows for the ~100 ms of its button
  animations and a covered overlay never recovers by itself. Primary monitor only —
  the shell does not reorder `Shell_SecondaryTrayWnd`.
- `ShellFlyout` hides the primary overlay while the shell's own flyouts are open.
  Then the bar sits above **every** topmost window: a brand-new topmost window is
  covered too, and `HWND_TOP`, `HWND_TOPMOST` re-asserts, `NOTOPMOST/TOPMOST`
  toggles, hide/show, `SetWindowBand` and `SetParent` into `Shell_TrayWnd` all fail
  to win (tried on 26200). Recognised by foreground class + owning process: Start and
  search (`Windows.UI.Core.CoreWindow` from `SearchHost`), quick settings
  (`ControlCenterWindow` from `ShellHost`), task view and the tray overflow
  (explorer's XAML islands). Raising the block above the bar during Start was tried
  and rejected — the jump looks worse than the hide.
- `ShowInFullscreen` (default off) is enforced by `OverlayWindow.Position` per
  monitor, second guard `Fullscreen.CoversForeground`, which must skip the desktop
  and the shell: clicking the desktop focuses `Progman`, it spans every monitor, and
  an unfiltered rect test hid all readings until a program was focused (1.4.1
  regression, fixed in 1.4.2).
- Windows does not move or hide `Shell_TrayWnd` for a full-screen program (same rect,
  still `WS_VISIBLE`; measured on 26200), so `TaskbarInfo.IsHidden` stays false and
  cannot be the gate. `Fullscreen.Refresh` runs on a 1 s timer, always measures, and
  uses `SHQueryUserNotificationState` (`QUNS_BUSY` / `QUNS_RUNNING_D3D_FULL_SCREEN`)
  plus a monitor-sized foreground window that ignores captioned, desktop and shell
  windows; when the shell reports full screen but the foreground is elsewhere it
  enumerates visible windows for the covering one, so only that monitor hides. Do
  not enumerate unconditionally: the NVIDIA overlay's `CEF-OSC-WIDGET` window is
  visible, captionless and monitor-sized on every desktop and would hide the
  readings for good. With the option on, the overlay reuses the last bar rect
  (`_lastBar`/`_lastTrayLeft`) or `HiddenBar()`. Transitions are logged as
  `fullscreen: …`.
- `MeterView` renders two rows from the fixed `AppSettings.LayoutOrder()`: Network
  spans both rows (upload over download), then CPU/GPU, RAM/Disk, Battery. A disabled
  reading keeps its slot; a fully empty column is dropped. `Stabilize()` raises the
  panel's `MinWidth` to the widest content since settings changed — the block is
  right-anchored, so without it every digit-width change shoves it sideways; only
  `Apply` resets it. Do not right-align or reserve per-widget slots (tried, looked
  wrong). The percentage is always the primary value and the warning-color source;
  `ShowAmount` gates RAM/Disk used/total, `ShowVram` the GPU memory, `ShowClock` the
  CPU frequency (admin-gated) and `ShowFan` the GPU fan. Warning colors come from
  `WarnPercent`/`CriticalPercent`/`WarnTempC`/`CriticalTempC`, set from Appearance →
  Readings/Warnings.
- The overlay panel's fill is `MeterView.HitTestFill` (black at alpha 1), not
  `Transparent`: a zero-alpha layered window is click-through, so right-click would
  stop working between the readings.
- Position uses device pixels: logical settings × `GetDpiForWindow`. Sliders are
  paired with numeric boxes via `BindNumber` (the slider owns the value); ranges are
  0–80 gap, 0–900 right offset, −30..40 vertical, matching `AppSettings.Normalize`.
- Widget names are fixed (`MetricKind.DefaultLabel()`; Network has none) — there is
  no custom label, do not reintroduce a `Label` setting. `MetricKind.Vram`,
  `DiskActivity`, `DiskSpace`, `NetUp`, `NetDown` are legacy values so old settings
  load; `Normalize` folds them into Gpu, Disk and Network.
- One Disk widget, one Drive picker; `Normalize` folds extra Disk blocks into the
  first. The snapshot still carries a `Disks` dictionary and `MetricsService` keeps
  one `DiskSampler` per drive because the graphs and alerts sit on it.
- Battery is force-disabled at startup when `BatterySampler.IsPresent()` is false.
- Network measures `AppSettings.NetAdapter` (empty = every active adapter, filtered
  against loopback, tunnels and the usual virtual NICs); an explicit pick bypasses
  the filters on purpose.
- UI label is **Widgets**, but code and XAML still say `Blocks` (`Tag="Blocks"`,
  `BlocksPage`, `BlocksPanel`, `BlockSettings`, `RebuildBlocks`) — rename user-facing
  strings only.
- `SettingsWindow.xaml` control names are used from the code-behind; widget rows, the
  inline options and the `OptionChip` switches are built in code (`BuildBlockRow`,
  `BuildOptions`, `OptionChip`) from `_selectedBlock`.
- Settings-window colors must be `DynamicResource` (styles may stay
  `StaticResource`): `Surface`, `Ink`, `Muted`, `LineSoft`, `Accent`, `Chip`,
  `RowHighlight`, `CardBg`, `CardBorder`, `FieldBorder`, `SectionFg`, `BadgeBg`,
  `BadgeInk`, `SwitchTrack`, `SliderTrack`, `HoverBg`, `PressedBg`. A new color needs
  a key in `SettingsWindow.xaml` **and** an entry in `Palette.AppColors()` for both
  themes, and styles must theme `Foreground` too (`SecondaryButton` shipped without
  it and was invisible in dark mode). Exception: the preview strip and its clock are
  set in code from `Palette.PreviewBarBackground()`/`palette.Value` because that mock
  follows the taskbar palette, not the window theme.
- Settings apply live and auto-save (400 ms debounce via `QueueSave`).
  `Settings.Theme` styles only the settings window (`ApplyChrome()` copies
  `Palette.AppColors(dark)`; Win10 cannot flip the caption back to light at runtime).
  The overlay ignores the theme and follows `SystemUsesLightTheme`.
- `History` (in `MetricsService.cs`) is a 120-sample ring; `GraphsWindow.cs` draws it
  as sparklines on a 1 s timer, opened by left-clicking the overlay (`App.ShowGraphs`,
  single instance), only while open.
- `Alerts.cs` evaluates every snapshot on the UI thread: a value above its critical
  threshold for a full minute raises one borderless popup near the primary tray plus
  `SystemSounds.Exclamation`, then stays suppressed until it recovers.
  `AlertsEnabled` gates it; `Normalize` clamps the thresholds to sane floors, so
  testing needs real load.
- Sensors: CPU temperature and frequency need administrator **and the PawnIO driver**
  (LibreHardwareMonitor 0.9.6 uses PawnIO; WinRing0 is gone, so no `.sys` next to the
  exe). Both are LHM sensors on one `Computer`, but `SampleTemperature`/
  `SampleClock` each call `_cpu.Update()` — keep further LHM reads off the per-tick
  path. GPU usage/VRAM/temperature/fan come from NVML; without NVML (Intel/AMD)
  `LhmGpu` reads the first GPU through LHM and the fan arrives as `GpuFanRpm`
  (`Fan 1200RPM`) — only exercised via `TASKBARMONITOR_GPU_SOURCE=lhm` on the NVIDIA
  box, untested on real Intel/AMD. Unavailable values are `NaN` and are omitted, not
  zeroed. Optimus GPUs error while asleep, so 0 % GPU is a deliberate fallback;
  `Fan 0%` is real (desktop cards stop at idle). There is no power (W) reading.
- Admin-gated options: turning on CPU Temperature or Frequency while not elevated
  prompts a restart (`SetAdminOption` → `App.RestartElevated`, relaunches with
  `--replace` and reopens settings); declining flips the switch back. GPU
  temperature/VRAM/fan are never gated. The expanded CPU/GPU row carries
  `BuildSensorHint`/`UpdateSensorHint` (2 s status timer): **Restart as
  administrator** and/or **Install the PawnIO driver**, the latter downloading
  `PawnIO_setup.exe` and running it `-install` elevated. It stays hidden until the
  user clicks a locked `OptionChip` (`_hintRevealed`). `PawnIoInstalled()` needs the
  `PawnIO` service key **and** `%ProgramFiles%\PawnIO\PawnIOLib.dll` (the official
  uninstaller leaves the key); the GPU hint needs `Metrics.GpuUsesLhm`.
  `DefaultBlocks` starts the CPU block with `ShowTemperature`/`ShowClock` off; GPU
  temperature stays on.
- Startup is the HKCU Run key, or a scheduled task with `/RL HIGHEST` when elevated
  startup is enabled (the shipped setup uses the task, so CPU temperature works from
  every logon with no UAC prompt). `Startup.Apply` **imports** an XML definition
  instead of `schtasks /Create`: the command line version sets "do not start on
  batteries" and a 72-hour limit, which would block laptops and kill the overlay
  after three days.
- Auto-update: `UpdateService` reads `releases/latest` (asset **must** stay
  `TaskbarMonitor.exe`) and `UpdateWindow` is the only progress surface. A running
  exe can be renamed but not overwritten, so `InstallAsync` downloads
  `TaskbarMonitor.exe.new`, verifies the API's sha256 digest, moves the old exe to
  `.old`, moves the new one in and starts it with `--replace`; the next start deletes
  `.old`. The daily check rides the 2 s `_themeTimer` and only pops the window when
  the app was not started with `--silent`.

## Sensor setup on a fresh machine

Temperatures silently stay hidden (no error, no `.sys`) until PawnIO is present.
Install the signed setup from <https://github.com/namazso/PawnIO.Setup/releases> with
`PawnIO_setup.exe -install` elevated (`/S` exits 87 and installs nothing), then run
the app elevated. A stray `TaskbarMonitor.sys` means an old build or the leftover
`R0TaskbarMonitor` driver service is still registered.
