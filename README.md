# Smart Plug Control

A [N.I.N.A.](https://nighttime-imaging.eu/) plugin to control TP-Link Kasa smart plugs and power
strips from your imaging sequences - turn equipment on/off, toggle indicator LEDs, and react to
plug state from the Advanced Sequencer.

## Why cloud-only, not local network?

This plugin talks to your devices **exclusively through the TP-Link cloud API** - it never connects
to a plug's IP address on your local network. This is a deliberate design choice, not a limitation:
Smart Plug Control is built to be safe in **multi-tenant commercial remote observatories**, where
several clients' equipment can share the same network. Some observatories isolate each client with a
VLAN, but that isn't guaranteed everywhere. A local-network driver would let any NINA instance on a
shared network scan for and control *any* device on it, regardless of which TP-Link account owns it.
Routing every command through TP-Link's cloud means TP-Link enforces the account boundary
server-side - your login can only ever control devices linked to your own account, independent of
network topology.

If you only ever run NINA on your own private home network, this makes no practical difference to
you other than requiring an internet connection for plug control.

## Requirements

- N.I.N.A. 3.0.0.2017 or newer.
- A TP-Link (Kasa) account with your smart plugs already set up in the Kasa app.
- **Kasa devices only for now.** Tapo devices are discovered and listed, but not yet controllable -
  see [Limitations](#limitations).

## Installation

Smart Plug Control isn't in NINA's official plugin repository yet, so for now it's a manual install -
NINA only auto-extracts a plugin zip when it downloads it itself from the official repository; a zip
placed here by hand has to be extracted by you first, NINA will not do it for you.

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
- Live power draw, on plugs/strips that support energy monitoring.
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
- Total Consumption Below / Total Consumption Above a wattage threshold (requires a plug that
  supports energy monitoring)

**Triggers:**
- Plug State Changed - warns if a plug's on/off state changes unexpectedly during the sequence (e.g.
  switched from the Kasa app or a physical button). It only notifies; it won't try to restore the
  previous state automatically. For a real remote-observatory alert (push notification, email) or to
  pause the sequence and run recovery instructions when this fires, wrap it in a
  [Sequencer+](https://github.com/palmito9/Nina.SequencerPlus) **DIY Trigger** together with a
  notification plugin like Ground Station for the actual push/email alert.

## Settings

**Options → Plugins → Smart Plug Control:**
- TP-Link account credentials.
- Consumption threshold (Watts) and preventive alert percentage - reserved for a future
  consumption-alert feature, not active yet.
- Equipment page refresh interval (seconds).

## Limitations

- **Tapo isn't supported yet.** Tapo devices show up in the plug list (discovery works for both
  brands on the same TP-Link account) but can't be turned on/off - Tapo's local protocol needs real
  hardware to confirm the cloud-relay approach works before it's built.
- **Consumption-based alerts aren't built yet.** The threshold/percentage settings exist but nothing
  reads them until that feature lands.
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

[Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/)
