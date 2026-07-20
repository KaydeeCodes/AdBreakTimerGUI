# Ad Break Timer

![Status](https://img.shields.io/badge/status-in%20development-yellow)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

A small Windows control panel that hosts two OBS Browser Source overlays (a bar and a radial ring) and drives them with simple URL commands. Built for Streamer.bot, but works with anything that can send an HTTP request.

Made by [Kaydee.Codes](https://kaydee.codes/). Free to use, no data collected, ever.

---

## Status

**This is still in early development and alpha testing, not yet released.** No version number, no build, nothing to download yet. Core functionality (the web server, both overlays, the GUI) is working and being tested, Twitch integration is planned but not started.

If you've stumbled on this repo early, feel free to poke around, but don't expect it to be stable or documented for general use just yet.

---

## What works so far

- A GUI control panel: start/stop the service, see its status, copy the overlay URLs
- Two overlays, a bottom bar and a radial ring, both driven by the same URL command API
- Settings for ad break length, ad free interval, and overlay appearance (direction, size, colours)
- System tray support, minimises instead of quitting
- Logging to a file for troubleshooting

## Not built yet

- Twitch EventSub integration (`channel.ad_break.begin`), so the app can auto detect ads without Streamer.bot
- An actual installer, currently only runs from source
- Full documentation

---

## Building from source

Needs the .NET 10 SDK. From inside the folder with `AdBreakTimerGUI.csproj`:

```bash
dotnet publish -c Release
```

The exe lands in `bin/Release/net10.0-windows/win-x64/publish/AdBreakTimerGUI.exe`.

---

## Config files

Everything lives under `%AppData%\AdBreakTimer`:

```
%AppData%\AdBreakTimer\
├── settings.json
├── bar.json
├── radial.json
└── latest.log
```

---

## API reference (quick version)

Base: `http://localhost:<port>/bar/api` or `/radial/api`. Every request returns JSON.

The main command:
```
cmd=go&t=01:00:00&color=%2300ff00&finish=%23ff0000&dir=drain
```

Other general commands: `start`, `pause`, `stop`, `reset`, `settime`, `addtime`, `subtime`, `setcolor`, `setfinishcolor`, `setbgcolor`, `setflash`, `setflashduration`, `status`.

Bar only: `setdirection` (`drain`/`fill`), `setbarheight`, `setbarwidth`.

Radial only: `setdirection` (`cw`/`ccw`), `setsize`, `setthickness`, `settrackcolor`.

A proper full reference table will go here once things settle down and this is closer to an actual release.

---

## Licence

Free to use, modify, and share. No attribution required, though it's appreciated. No warranty, use at your own risk.

---

Made by [Kaydee.Codes](https://kaydee.codes/). Free to use, no data collected, ever.
