# Smart Plug Control

A [N.I.N.A.](https://nighttime-imaging.eu/) plugin to control TP-Link Kasa and Tapo smart plugs and
power strips from your imaging sequences - turn equipment on/off, toggle indicator LEDs, and react
to plug state from the Advanced Sequencer.

## How this stays safe on a shared observatory network

Smart Plug Control is built for **multi-tenant commercial remote observatories**, where several
clients' equipment can share the same network - not just backyard/personal setups. Your TP-Link
account is always the authoritative device list (you already have to add a plug to your Kasa/Tapo
account to use it at all), and every discovered device is checked for physical presence on this PC's
local network before it's shown or controlled at all - a plug on your account but at a different
site (a different observatory, home) never shows up here.

How a present device is actually controlled then depends on its protocol, not a setting you choose:
- **Older-generation Kasa devices** (e.g. HS103, KP303) are controlled through the TP-Link cloud
  relay. This protocol has no local authentication at all, so routing it through the cloud lets
  TP-Link enforce the account boundary server-side - your login can only ever control devices linked
  to your own account, independent of network topology.
- **Tapo devices and newer-generation Kasa devices** (e.g. the KP125M) use TP-Link's newer
  KLAP/securePassthrough protocol, which has no cloud-relay path at all - every implementation
  (official app included) talks directly to the device's local IP. This is still safe for the
  multi-tenant threat model: the KLAP handshake itself is verified by the device against a hash of
  your actual TP-Link account credentials (stored on the device since it was paired), so another
  tenant on the same network can't control your device without knowing your account password.

If you only ever run NINA on your own private home network, none of this changes how you use the
plugin day to day.

## Requirements

- N.I.N.A. 3.0.0.2017 or newer.
- A TP-Link account (Kasa or Tapo) with your smart plugs already set up in the corresponding app.

## Installation

Smart Plug Control is listed in NINA's official plugin repository - the easiest way to install it:

1. In NINA, go to **Options → Plugins**, find **Smart Plug Control** in the list, and click install.
   NINA downloads and extracts it for you.
2. Restart NINA if prompted.

### Manual install (a specific version, or a build from source)

1. Download and **extract** the zip from the
   [Releases](https://github.com/crepusculum-space/nina.plugin.smartplugcontrol/releases) page (or
   build it yourself, see [Building from source](#building-from-source)).
2. Close NINA completely if it's running (check Task Manager too - closing the window doesn't always
   kill the process, and a still-running NINA will keep a lock on files you're trying to replace).
3. Make sure `%LOCALAPPDATA%\NINA\Plugins\3.0.0\` exists (create it if it doesn't), then place the
   extracted plugin folder (containing `Crepusculum.NINA.SmartPlugControl.dll` and its dependencies)
   directly inside it - e.g.
   `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Crepusculum.NINA.SmartPlugControl\Crepusculum.NINA.SmartPlugControl.dll`.
   Don't just copy the zip file itself into that folder - it must be extracted first.
4. Start NINA. The plugin appears under **Options → Plugins** as "Smart Plug Control".

## Setup

1. Open **Options → Plugins → Smart Plug Control**.
2. Enter your TP-Link account username/email and password. The password is encrypted on disk and
   never shown in plain text.
3. Click **Refresh Plug List** to pull every device on your account. Your TP-Link account may have
   devices unrelated to your observatory (a home TV, a printer) - uncheck **Visible in NINA** for
   any plug that shouldn't show up in the equipment page or sequencer.

## Equipment page

A dockable panel in NINA's **Imaging** tab (toolbar icon to show/hide it without leaving the Imaging
view) lists every visible plug with:

- Live on/off status and a toggle to switch it.
- An editable **Equipment** name (e.g. "Mount", "Dew heater") so plugs are easy to identify.
- LED toggle, for plugs/strips that have one.
- A **Protected** lock - a protected plug can't be turned off from the sequencer at all, and
  requires two confirmations here in the equipment page.
- Live power draw, on plugs/strips that support energy monitoring, alongside an estimated Amps
  figure (see Settings below for the line voltage used).
- On monitoring-capable plugs, two more fields: **Max (A@12V)** and **PSU eff (%)** - the rated max
  draw (in Amps at 12V DC) of whatever's plugged in there (e.g. a Pegasus Powerbox) and an estimated
  power-supply efficiency, used by the consumption trigger/loop conditions below. These are properties
  of the equipment itself, essentially invariant, so they're configured once here rather than
  per-sequence-item - set them once when you plug something in and forget about them.
- Bulk **All Plugs On/Off** and **All LEDs On/Off** buttons (protected plugs are skipped by "All
  Plugs Off").

## Sequencer support

All of the following live under the **Smart Plug Control** category.

**Instructions:**
- Turn Plug On / Turn Plug Off (with an optional delay after turning on)
- Turn All Plugs On / Turn All Plugs Off (same optional delay)
- Turn LED On / Turn LED Off
- Turn All LEDs On / Turn All LEDs Off

Turning off a plug marked **Protected** is hard-blocked here with a validation error - the sequence
won't start until it's removed or the plug is unprotected.

**Loop Conditions** (shown under NINA's own "Loop Conditions" section, not a separate "Conditions"
group - that's how NINA categorizes all conditions):
- Plug Is On / Plug Is Off
- Consumption Below / Consumption Above - true as long as a **specific selected plug's** own power
  draw stays below/above a preventive alert percentage (configured right on the condition) of that
  plug's own max threshold (Max A@12V / PSU eff %, configured once on the equipment page - see above).
  Deliberately per-plug, not a sum across every plug: different equipment has a different rated max,
  so a single combined number wouldn't mean anything actionable (e.g. an 8A-rated device and a
  12A-rated device on the same site need different thresholds).

**Triggers:**
- Plug State Changed - warns if a plug's on/off state changes unexpectedly during the sequence (e.g.
  switched from the Kasa app or a physical button).
- Consumption Threshold Changed - same per-plug design as the loop conditions above: pick a plug and
  a preventive alert percentage right on the trigger, so different sections of the same sequence can
  use a different tolerance for the same plug (e.g. more tolerant during a brief flats routine with a
  light panel drawing extra current on the same circuit).

**All three consumption-related sequence items are notification-only by design - none of them takes
any corrective action, and none of them ever will.** A plug's power state, and how much your equipment
draws, are treated as human decisions in this plugin, not something it should act on automatically -
especially since a smart plug can only ever see its own whole circuit; it has no visibility into
what's actually plugged in downstream (e.g. which port of a Pegasus Powerbox is drawing the most
current), so there's no generically-correct automatic response to "consumption is high" anyway. For a
real remote-observatory alert (push notification, email) or to pause the sequence and run recovery
instructions yourself when a trigger fires, wrap it in a
[Sequencer+](https://github.com/palmito9/Nina.SequencerPlus) **DIY Trigger** together with a
notification plugin like Ground Station for the actual push/email alert.

## Settings

**Options → Plugins → Smart Plug Control:**
- TP-Link account credentials.
- Line voltage (V) - used only to show an estimated Amps figure next to the Watts reading in the
  equipment page's Power column (e.g. "4.4 W / 0.04 A"). Purely a display convenience (P = V × I on
  the AC side) - unrelated to the Max A@12V consumption-alert threshold on the equipment page, which
  is a different voltage domain entirely (downstream DC equipment, not the AC line).
- Equipment page refresh interval (seconds).

## Limitations

- **The Power reading (Watts and the derived Amps figure) is a ballpark indicator, not a precise
  measurement - don't build automation logic around exact values.** In real testing, a Tapo P115
  reading the same constant load fluctuated between 4.4 and 4.7 W from one refresh to the next, and
  the Amps figure is a further estimate on top of that (see the in-app explanation of the Watts↔Amps
  math). Use the consumption triggers/conditions for a rough "is this roughly where I expect it to
  be" signal and manual notification only - not for anything that needs to be exact.
- **Tapo/newer-Kasa LED control isn't available.** No LED-related field has been found in these
  devices' status response, unlike legacy Kasa.
- A plug's on/off state is only re-checked by the sequencer between instructions, never while one is
  running - a trigger can't react mid-instruction to a change that happens during a long exposure or
  wait.

## Building from source

```
dotnet build SmartPlugControl/SmartPlugControl.csproj
```

NINA must be fully closed while building - the build's post-build step copies the output straight
into `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Crepusculum.NINA.SmartPlugControl\`, and it will fail loudly
(rather than silently deploy nothing) if NINA still has the DLL locked.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

[GPL-3.0-only](LICENSE)

Part of `SmartPlugControlCloud/KasaCloudPassthroughClient.cs` (the legacy-Kasa cloud passthrough
protocol) was reverse-engineered by consulting two GPL-3.0-licensed reference implementations
([piekstra/tplink-cloud-api](https://github.com/piekstra/tplink-cloud-api) and
[python-kasa](https://github.com/python-kasa/python-kasa)) - this plugin is licensed as GPL-3.0-only
as a whole to stay compatible with that.

## Third-party licenses

This plugin bundles [TapoConnect](https://github.com/cwakefie27/TapoConnect) and
[BouncyCastle.Crypto](https://www.bouncycastle.org/csharp/) (both MIT-licensed) in its distributed
build - MIT-licensed code is compatible with inclusion in a GPL-3.0 work. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for their full license texts and copyright notices.
