# AGENTS.md

Portable Windows taskbar monitor: one WPF app in `TaskbarMonitor/`; the shipped
build is `dist/TaskbarMonitor.exe` (self-contained single file, ~141 MB).

Not a git repo — no commits, no branches. `dist/` is the release output and
`TaskbarMonitor/.gitignore` (bin/obj/dist) exists for a future repo, so
publishing is the only way changes reach users.

Verified on Windows 11 (26200) and Windows 10 22H2 (19045); multi-monitor and
vertical taskbars are implemented but untested.

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
- No tests exist. Verify by building, launching, and watching the real taskbar
  (`MetricsService` holds the live snapshot). UIA inspection only works on a
  non-elevated instance — UIPI blocks UI Automation into an elevated one.
  Practical automation: screenshot the taskbar, read
  `%AppData%\TaskbarMonitor\settings.json`, drive WPF ComboBoxes with
  `SetFocus` + `SendKeys` (`{F4}`, type the item, `{ENTER}`), and set the
  Position boxes with `ValuePattern.SetValue`. Synthetic clicks do not reach
  native MessageBox buttons; post `WM_COMMAND` with `IDNO` (7) instead.

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
- x64 only (`GetWindowLongPtrW`, NVML).

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
  It caches the taskbar handle and polls every 500 ms; do not add per-tick
  `UpdateLayout()`. `RedrawWindow` on move/resize/close prevents ghost pixels.
  Placement is clamped inside the taskbar and left of the tray, and the tray gap
  applies to manual offsets too.
- `ShowInFullscreen` (default off) keeps the overlay visible while the shell has
  hidden the taskbar for a full-screen program. `Fullscreen.Refresh` runs on a 1 s
  timer and uses `SHQueryUserNotificationState` (`QUNS_BUSY` /
  `QUNS_RUNNING_D3D_FULL_SCREEN`) with a foreground-window fallback that ignores
  captioned, desktop and shell windows. The overlay then reuses the last seen
  taskbar rect (`_lastBar`/`_lastTrayLeft`), or `HiddenBar()` when it started
  during full-screen, and re-asserts `HWND_TOPMOST` every tick.
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
  Visibility lives only in the Widgets switches. The percentage is always the
  primary value (and the warning-color source); `ShowAmount` gates the RAM/Disk
  used/total extra, `ShowVram` the GPU memory.
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
- Settings apply live and auto-save (400 ms debounce via `QueueSave`).
  `Settings.Theme` styles only the settings window: `ApplyChrome()` copies
  `Palette.AppColors(dark)` into those keys (sidebar and dark title bar stay
  fixed; Win10 cannot flip the caption back to light at runtime). The overlay
  ignores the theme and always follows `SystemUsesLightTheme`;
  `palette.Label/Value/…` only color the text, and there are no custom color
  settings.
- Sensors: CPU temperature needs administrator (LibreHardwareMonitor driver); GPU
  needs NVIDIA NVML. Unavailable values are `NaN` and are omitted rather than
  zeroed. Optimus GPUs error while asleep, so 0 % GPU is a deliberate fallback.
- Admin-gated options: turning on CPU Temperature or Power while not elevated
  prompts a restart (`SetAdminOption` → `App.RestartElevated`), which relaunches
  with `--replace` and reopens the settings window. Declining flips the switch
  back off. GPU temperature/power/VRAM come from NVML and are never gated.
- Startup is the HKCU Run key, or a scheduled task with `/RL HIGHEST` when
  elevated startup is enabled.
