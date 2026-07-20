# Ad Break Timer

![Version](https://img.shields.io/badge/version-1.2.0-orange)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Type](https://img.shields.io/badge/type-single%20exe-blue)
![Licence](https://img.shields.io/badge/licence-free%20to%20use-brightgreen)

A small Windows control panel that hosts two OBS Browser Source overlays (a bar and a radial ring) and drives them with simple URL commands. Built for Streamer.bot, but works with anything that can send an HTTP request.

No install beyond a normal per-user setup, no database, no external files. Everything it needs is baked into the exe, and the only things it writes to disk live in `%AppData%\AdBreakTimer`.

Made by [Kaydee.Codes](https://kaydee.codes/). Free to use, no data collected, ever.

---

## New here? You don't need to read this whole page

1. Download the latest release and run the installer (or `AdBreakTimerGUI.exe` if you built it yourself).
2. The window opens and the service starts on its own.
3. Copy the **Bar overlay** or **Radial overlay** link and add it as an OBS Browser Source.
4. Set your ad break length and ad free interval under **Ad timing**, then click **Save settings**.
5. Paste the **Example go command** link into Streamer.bot (see [Streamer.bot setup](#streamerbot-setup) below for the full pattern).

That's genuinely the whole thing. Everything below this point is reference material for later.

---

## Contents

- [New here?](#new-here-you-dont-need-to-read-this-whole-page)
- [Features](#features)
- [The main window](#the-main-window)
- [Overlay settings](#overlay-settings)
- [Building from source](#building-from-source)
- [OBS setup](#obs-setup)
- [Config files](#config-files)
- [API reference](#api-reference)
  - [The `go` command](#the-go-command)
  - [General commands](#general-commands)
  - [Bar only commands](#bar-only-commands)
  - [Radial only commands](#radial-only-commands)
  - [Response format](#response-format)
- [Common commands](#common-commands)
- [Behaviour when a countdown finishes](#behaviour-when-a-countdown-finishes)
- [Streamer.bot setup](#streamerbot-setup)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)
- [Licence](#licence)

---

## Features

- **A proper control panel, not a console window.** Start and stop the service, see its status at a glance, and copy every URL you need with one click.
- **Traffic light status.** Green means running with no problems, amber means running but something's logged an error worth a look, red means stopped.
- **System tray support.** Closing the window minimises it to the tray rather than quitting, so it can sit quietly in the background while you stream. Right click the tray icon for Start/Stop/Exit without reopening the window.
- **Start automatically on launch**, if you want it running the moment Windows starts the app, no extra clicks needed.
- **Two overlays in one app.** A bottom progress bar and a circular progress ring, each independently controlled with its own config and its own API.
- **A settings window for overlay appearance.** Direction, bar height and width, ring size, thickness, and track colour, all editable without touching a config file by hand.
- **Fully responsive overlays.** No fixed canvas size. Resize the OBS Browser Source to whatever dimensions you want and both overlays fill it correctly.
- **Automatic port handling.** Tries the last known good port first, walks upward if it's taken, and remembers the working port for next time.
- **One shot control.** A single `go` command sets the colour, direction, and duration and starts the countdown, instead of chaining several requests together.
- **Self healing finish state.** When a countdown hits zero, the overlay flashes full width or a full ring in the finish colour for a configurable duration, then automatically clears itself back to idle. It never gets stuck lit up if the next command is late.
- **A log file for support.** `latest.log` is overwritten fresh every run. There's an **Open log file** button right on the main window, so if something's not working the fix is just "send me that file".
- **No telemetry.** Nothing is sent anywhere except localhost.

---

## The main window

| Section | What it does |
|---|---|
| Status row | The traffic light, current status, listening port, and the Start/Stop button. |
| Start automatically on launch | Starts the service the moment the app opens, no manual click needed. |
| Minimise to tray when closed | The window's X button hides to the tray instead of quitting. Untick this if you'd rather it close normally. |
| Overlay & API links | The Bar overlay URL, Radial overlay URL, and an example `go` command, each with a Copy button. |
| Ad timing | Your ad break length and ad free interval, in `mm:ss` or `hh:mm:ss`. Click Save settings to apply. |
| Overlay appearance | Opens the [Overlay settings](#overlay-settings) window. |
| Twitch account | Not yet available, see [Roadmap](#roadmap). |
| Open config folder / Open log file | Jump straight to `%AppData%\AdBreakTimer` or the current log, no manual navigation needed. |

---

## Overlay settings

Click **Bar / Radial settings...** on the main window to open a small dialog with two tabs:

**Bar tab**
- Fill direction (`drain` starts full and shrinks, `fill` starts empty and grows)
- Bar height, in pixels
- Bar width, any CSS width value (`100%`, `800px`, and so on)

**Radial tab**
- Direction (`cw`/`ccw`)
- Size, as a percentage of the viewport's smaller side
- Thickness, as a percentage of the diameter
- Track colour, the unfilled background ring

Changes only take effect once you click **Save**, closing with Cancel or the X leaves everything untouched.

---

## Building from source

Needs the .NET 10 SDK. From inside the folder with `AdBreakTimerGUI.csproj`:

```bash
dotnet publish -c Release
```

The exe lands in `bin/Release/net10.0-windows/win-x64/publish/AdBreakTimerGUI.exe`, self-contained, nothing else needs to travel with it.

---

## OBS setup

1. In OBS, add a **Browser Source**.
2. Set the URL to whichever overlay you want, copied straight from the main window (e.g. `http://localhost:37000/bar/`).
3. Set the width and height to whatever fits your scene, both overlays are fully responsive and will fill whatever space they're given.
4. Leave everything else at its defaults.

---

## Config files

Everything lives under `%AppData%\AdBreakTimer` (a normal user folder, no admin rights needed):

```
%AppData%\AdBreakTimer\
├── settings.json    (port, ad timing, start on launch, minimise to tray)
├── bar.json         (current state and appearance of the bar overlay)
├── radial.json      (current state and appearance of the radial overlay)
└── latest.log       (full diagnostic log, overwritten fresh every launch)
```

The **Open config folder** button on the main window goes straight there.

---

## API reference

Base: `http://localhost:<port>/bar/api` or `/radial/api`. The port is shown on the main window, `37000` is the default if nothing's taken it.

### The `go` command

The one I actually use day to day. One request sets whatever's given and starts the countdown straight away, instead of chaining several calls.

```
cmd=go&t=01:00:00&color=%2300ff00&finish=%23ff0000&dir=drain&flash=on&flashfor=30
```

| Parameter | Required | Description |
|---|---|---|
| `t` | Yes | Duration, `hh:mm:ss`, `mm:ss`, or a plain number of seconds. |
| `color` | No | Running colour. |
| `finish` | No | Colour when it hits zero. |
| `dir` | No | Bar: `drain`/`fill`. Radial: `cw`/`ccw`. |
| `flash` | No | `on`/`off`, whether it flashes on finish. |
| `flashfor` | No | Seconds to flash before auto clearing to idle. |

### General commands

| Command | Parameters | Description |
|---|---|---|
| `cmd=start` | none | Resumes from whatever time is currently set. |
| `cmd=pause` | none | Freezes the countdown in place. |
| `cmd=stop` | none | Clears back to idle, time reset to zero. |
| `cmd=reset` | none | Back to idle, but restores the original time rather than clearing it. |
| `cmd=settime` | `t` | Sets the time without starting it. |
| `cmd=addtime` | `s` (seconds) | Adds time to the current countdown. |
| `cmd=subtime` | `s` (seconds) | Removes time from the current countdown. |
| `cmd=setcolor` | `v` (colour) | Sets the running colour. |
| `cmd=setfinishcolor` | `v` (colour) | Sets the colour used when the countdown hits zero. |
| `cmd=setbgcolor` | `v` (colour, or `transparent`) | Sets the page background colour. |
| `cmd=setflash` | `v` (`on`/`off`) | Turns the finish flash animation on or off. |
| `cmd=setflashduration` | `v` (seconds) | How long it flashes before auto clearing to idle. |
| `cmd=status` | none | No op. Returns the current state, used by the overlay page's own polling. |

Colours accept hex (with or without `#`), CSS named colours, `rgb()`/`rgba()`/`hsl()`/`hsla()`, or `transparent`.

### Bar only commands

| Command | Parameters | Description |
|---|---|---|
| `cmd=setdirection` | `v` (`drain`/`fill`) | `drain` starts full and shrinks, `fill` starts empty and grows. |
| `cmd=setbarheight` | `v` (pixels) | Height of the bar. Default `5`. |
| `cmd=setbarwidth` | `v` (CSS width, e.g. `100%`) | Width of the bar track. |

### Radial only commands

| Command | Parameters | Description |
|---|---|---|
| `cmd=setdirection` | `v` (`cw`/`ccw`) | Sweep direction. |
| `cmd=setsize` | `v` (5 to 100) | Diameter as a percentage of the viewport's smaller side. Default `60`. |
| `cmd=setthickness` | `v` (1 to 50) | Ring stroke width as a percentage of the diameter. Default `7`. |
| `cmd=settrackcolor` | `v` (colour) | Colour of the unfilled background ring. |

### Response format

Every request returns JSON.

Success (state is trimmed here for readability, the real response includes every field for that overlay type):

```json
{
  "ok": true,
  "cmd": "go",
  "state": {
    "remaining": 1800,
    "initialTime": 1800,
    "status": "running",
    "color": "#00ff00",
    "finishColor": "#ff0000",
    "direction": "drain",
    "flashOnFinish": true,
    "flashDuration": 30
  }
}
```

Failure:

```json
{
  "ok": false,
  "error": "Time must be greater than zero."
}
```

---

## Common commands

Ready to paste, swap `37000` for your actual port if it's different (shown on the main window, also copyable directly from the **Overlay & API links** section).

**Start a 3 minute red ad break countdown:**
```
http://localhost:37000/bar/api?cmd=go&t=3:00&color=%23ff0000&dir=drain
```

**Start a 1 hour green countdown to the next ad break:**
```
http://localhost:37000/bar/api?cmd=go&t=1:00:00&color=%2300ff00&finish=%23ff0000&dir=drain
```

**Pause the current countdown:**
```
http://localhost:37000/bar/api?cmd=pause
```

**Resume it:**
```
http://localhost:37000/bar/api?cmd=start
```

**Add 30 seconds to whatever's currently running:**
```
http://localhost:37000/bar/api?cmd=addtime&s=30
```

**Clear it back to idle:**
```
http://localhost:37000/bar/api?cmd=stop
```

**Using the radial ring instead of the bar?** Swap `/bar/api` for `/radial/api` in any of the above, everything else stays the same.

**Just want to see it working right now**, without touching Streamer.bot at all? Paste this into any web browser's address bar while the app is running:
```
http://localhost:37000/bar/api?cmd=go&t=0:30&color=%2300ff00&finish=%23ff0000
```
That starts a 30 second green countdown that flashes red when it finishes.

---

## Behaviour when a countdown finishes

The instant a countdown hits zero, the overlay snaps to full (the whole bar, or a fully drawn ring) in the finish colour and flashes for `flashDuration` seconds. If nothing tells it what to do next before that duration is up, it automatically reverts to idle (invisible) on its own. It is never left stuck lit up waiting for the next command.

---

## Streamer.bot setup

A typical hour long ad break cycle with a 3 minute ad break in the middle, as a chain of `go` calls:

```
# When the previous ad break finishes, start a 1-hour countdown to the next one
GET http://localhost:37000/bar/api?cmd=go&t=01:00:00&color=%2300ff00&finish=%23ff0000&dir=drain

# When Streamer.bot detects the 3-minute ad break starting
GET http://localhost:37000/bar/api?cmd=go&t=00:03:00&color=%23ff0000&dir=drain

# When that ad break's Streamer.bot timer ends, back to the normal 1-hour countdown
GET http://localhost:37000/bar/api?cmd=go&t=01:00:00&color=%2300ff00&finish=%23ff0000&dir=drain
```

If a command is sent a little late, the overlay just flashes red at zero and clears itself, no manual cleanup needed. See [Behaviour when a countdown finishes](#behaviour-when-a-countdown-finishes).

**Adding the Fetch URL sub-action:** in Streamer.bot, search for **Fetch URL** when adding a new sub-action, paste the full `go` command as the URL, and leave the method as `GET`. No headers or request body needed.

**Why a delay before the follow up command doesn't need to match exactly:** Streamer.bot's own timers and the countdown running inside Ad Break Timer are two separate clocks. They don't need to match, if one finishes a few seconds before or after the other, the overlay just flashes its finish colour and quietly clears itself once it reaches zero, so a little drift never looks broken on stream.

---

## Troubleshooting

**If you're stuck, the easiest thing to send for help is the log.** Click **Open log file** on the main window. It's overwritten fresh every time the app starts, and every line's timestamped, which makes timing issues easy to actually pin down instead of guessing.

| Problem | Fix |
|---|---|
| Traffic light is amber | The service is running fine, but something's logged an error since it last started. Check the log file for an `[ERROR]` line. |
| Traffic light is red but I expected green | The service isn't running, click **Start service**, or check **Start automatically on launch** if you'd rather it start on its own next time. |
| Nothing shows up in OBS | Confirm the traffic light is green, and that the Browser Source URL matches exactly what's shown under Overlay & API links. |
| Bar or ring never moves | Make sure `cmd=go` was called, or `cmd=settime` followed by `cmd=start`. `cmd=status` alone does not start anything. |
| Want to change a default without an API call | Use the **Bar / Radial settings...** window on the main screen, or edit `bar.json`/`radial.json` directly in the config folder while the app is closed. |
| "Embedded resource not found" when opening an overlay page | The exe wasn't built correctly. If you built it yourself, run `dotnet clean` then `dotnet publish -c Release` again. |
| Lost track of the config folder | Click **Open config folder** on the main window. |

---

## Roadmap

**Connecting directly to Twitch.** Currently in progress. The plan is to use Twitch's `channel.ad_break.begin` EventSub subscription, which fires the moment a midroll ad starts and includes its duration, so the app can start the red countdown itself with no Streamer.bot trigger needed, then automatically switch to the green countdown once it's done. The **Auto detect ads** checkbox and **Twitch account** section already exist on the main window as a preview of where this is heading, both stay disabled until the connection itself is actually built.

**Time drift verification.** A possible future addition to compare the countdown against real elapsed time (via NTP) after the fact, mainly to catch the rare case where a long countdown drifts from the clock, rather than a day to day feature. Not started, only noted here so it isn't forgotten.

---

## Licence

Free to use, modify, and share. No attribution required, though it's appreciated. No warranty, use at your own risk.

---

Made by [Kaydee.Codes](https://kaydee.codes/). Free to use, no data collected, ever.
