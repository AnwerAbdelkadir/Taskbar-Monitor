# Taskbar Monitor

Live system readings on the Windows taskbar, next to the tray icons:

```
↑ 0.2 Mbps   CPU 42% (68°C)   RAM 40% (6.4/15.8G)
↓ 7.2 Mbps   GPU 12% (52°C VRAM 1.2/4.0G)   DISK 3% (54.2/128G)
```

Works on Windows 10 and Windows 11 (x64). No installation, no background services.

## What it reads

| Reading | Source |
|---|---|
| CPU usage | `GetSystemTimes` deltas |
| CPU temperature / package power | LibreHardwareMonitor (needs administrator) |
| RAM used / total | `GlobalMemoryStatusEx` |
| GPU usage, VRAM, temperature, power | NVIDIA NVML (driver `nvml.dll`) |
| Network upload / download | per-adapter byte counters |
| Disk activity / space | PDH `PhysicalDisk` counters + `DriveInfo` |
| Battery | `GetSystemPowerStatus` |

GPU values need an NVIDIA card. Without one the GPU blocks show `--`; everything
else keeps working. CPU temperature additionally needs the app to run as
administrator; without it the temperature is simply omitted.

## Run

```
TaskbarMonitor.exe             start with the settings window
TaskbarMonitor.exe --silent    start the overlay only
```

Right-click the readings on the taskbar to open settings, pause, toggle Windows
startup, or exit. Running the exe again while it is running opens the settings
window (single instance).

Settings and the log live in `%AppData%\TaskbarMonitor`.

## Settings

- **Widgets** – enable/disable every reading; show/hide the name, icon,
  temperature, VRAM, used/total amount and power. The layout is fixed: Network,
  then CPU/GPU, RAM/Disk and Battery. Percentages are always the main value.
  Switching on CPU temperature or power on a standard-user run offers to restart
  the app as administrator.
- **Appearance** – theme of the settings window (auto/light/dark), font and size,
  spacing and network unit (Mbps or MB/s). The taskbar readings have no background
  panel; they draw straight onto the taskbar.
- **Position** – all monitors or one monitor, gap from the tray, offset from the
  right edge, vertical offset, and a Reset button. Values can be typed as well as
  dragged; `0` offset means "just left of the tray icons", and the gap applies to
  both automatic and manual placement. The block is clamped inside the taskbar so
  it never overlaps the tray or leaves the bar. **Show over full-screen apps**
  keeps the readings on top while a game, video or browser covers the taskbar; it
  is off by default and only applies while Windows has hidden the taskbar.
- **Startup** – start with Windows, optionally elevated through a scheduled task
  (Windows then does not ask for permission on every logon).

Values turn amber above 80% and red above 92%. Temperatures turn amber at 85 °C
and red at 95 °C.

## Build

Requires the .NET 8 SDK.

```
dotnet build TaskbarMonitor.csproj -c Release
dotnet publish TaskbarMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

The published folder is portable: copy it anywhere and run `TaskbarMonitor.exe`.

## Notes and limits

- The overlay is a transparent topmost window positioned over the taskbar
  (`Shell_TrayWnd` + `TrayNotifyWnd`), not a real taskbar toolbar. That is what
  keeps it working on the Windows 11 XAML taskbar. The readings have no panel
  background, so the taskbar shows through. On Windows 11 the tray is measured
  through UI Automation, because `TrayNotifyWnd` is empty there.
- It hides itself while the taskbar is auto-hidden, and follows taskbar moves and
  DPI changes. With **Show over full-screen apps** on, it stays in the same place
  instead of hiding when the shell hides the taskbar for a full-screen program;
  `Fullscreen` decides that from `SHQueryUserNotificationState` plus the
  foreground window, and the overlay re-asserts topmost each tick.
- Vertical taskbars get a floating block beside the taskbar instead of a rotated
  layout.
- CPU temperature depends on the LibreHardwareMonitor kernel driver. If Windows
  blocks the driver, the reading stays hidden; CPU usage still works.
- On a desktop with no battery, the battery block is force-disabled and its
  switches are locked.
- The settings window follows its own theme; the dark title bar and sidebar stay
  dark in both themes. Text colors of the readings follow the Windows taskbar
  light/dark setting.
