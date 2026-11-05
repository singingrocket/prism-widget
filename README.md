# Prism

A small floating glass widget for Windows 11, built with WinUI 3. It shows the time, date, time zone and battery level, and a tiny audio spectrum analyzer along the bottom edge. The glass is styled after the frosted look of recent macOS versions.

```
┌──────────────────────────────┐
│ 10:42 AM            ▭ 78% ⚡│
│ Thursday, November 5         │
│ India Standard Time·UTC+05:30│
│▁▂▆▃▂▄▅▃▂▁▁▂▃▂▁▁▂▃▁▂▅▃▂▁ │
└──────────────────────────────┘

```

<!-- Add a screenshot here, e.g. ![Prism](docs/screenshot.png) -->

## Features

- **Glass look**: Windows Acrylic blur with a light tint, white highlight, soft edge and rounded corners.
- **Clock**: time, full date and local time zone with its UTC offset. Updates automatically when the time zone or daylight saving changes.
- **Battery**: always shown in the top-right corner. The bar is green, then orange at 20% or below, then red at 10% or below. ⚡ appears while charging.
- **Audio spectrum**: 40 bars that react to what your speakers are playing, or to your microphone. The strip fades in with sound and out when it's quiet.
- **Floating window**:
  - Drag it anywhere; it remembers its position.
  - Always on top, or a normal window.
  - Lock position.
  - Click-through, so clicks pass to the windows underneath.
  - Four transparency levels.
- **Tray icon**: show or hide the widget, change options, or exit.
- **Start with Windows**: optional.
- Hidden from the taskbar and Alt-Tab. Only one copy runs at a time. Works across multiple monitors with different display scaling.

## Requirements

- Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source

## Build and run

```powershell
git clone https://github.com/singingrocket/prism-widget.git
cd prism-widget\Prism
dotnet run
```

To make a standalone copy for everyday use:

```powershell
dotnet publish -c Release -o C:\Prism
C:\Prism\Prism.exe
```

If you want **Start with Windows**, turn it on from the published copy. Prism registers whichever `.exe` is running when you enable it.

## Using Prism

| Action | How |
|---|---|
| Move the widget | Drag it with the left mouse button |
| Options menu | Right-click the widget, or hover and click ⚙ |
| Always on top on/off | Hover and click 📌, or use the menu |
| Spectrum source | Menu → **Spectrum → Off / Speakers / Microphone** |
| Transparency | Menu → **Transparency → Frosted / Medium / Clear / Crystal** |
| Show / hide | Left-click the tray icon |
| Undo click-through | Right-click the tray icon → **Click-through** |
| Exit | Menu or tray icon → **Exit** |

Settings (position, options, spectrum source) are saved in `%LOCALAPPDATA%\Prism\settings.json`.

### About the audio spectrum

- **Speakers** (default): listens to whatever Windows is playing. It doesn't use the microphone and needs no permission.
- **Microphone**: listens to the default microphone. While it's active, Windows shows the microphone-in-use icon. On some Bluetooth headsets, turning on the mic switches them to lower-quality call audio.
- Audio is analysed in memory only. Nothing is recorded or sent anywhere.
- Capture pauses while the widget is hidden, and follows you when the default audio device changes.

## Project structure

```
Prism/
  App.xaml(.cs)                   App start-up, single-instance check, shared styles
  MainWindow.xaml(.cs)            The widget: layout, clock, battery, dragging, menus
  MainWindow.Spectrum.cs          Spectrum strip: bars, fade in/out, source selection
  Audio/AudioCapture.cs           Speaker (loopback) and microphone capture via Windows Core Audio
  Audio/SpectrumAnalyzer.cs       FFT and log-spaced frequency bands with smoothing
  Audio/CoreAudioInterop.cs       Windows Core Audio (WASAPI) definitions
  AlwaysActiveAcrylicBackdrop.cs  Acrylic that stays translucent when the window isn't focused
  TrayIcon.cs                     Notification-area icon and menu
  NativeMethods.cs                Win32 calls (rounded window shape, click-through, cursor)
  WidgetSettings.cs               Saved settings and the Start with Windows entry
mockups/                          HTML design mockups used while designing the widget
```

## Licence

[MIT](LICENSE)
