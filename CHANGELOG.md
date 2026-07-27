# Changelog

All notable changes to Smart Plug Control are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.0.0.2] - 2026-07-27

### Added

- Local-network control for Tapo devices and newer-generation Kasa devices (e.g. the KP125M) via the
  KLAP protocol - these were previously listed but not controllable. Discovery still goes entirely
  through your TP-Link account; a new local-presence check (ARP-based) filters that list down to only
  devices physically reachable from this PC, then routes control by protocol: legacy Kasa still goes
  through the cloud relay (unchanged), KLAP-family devices are now controlled directly over the local
  network. See the README for why this is still safe on a shared observatory network.
- As a side effect of the local-presence check: a plug on your TP-Link account but physically at a
  different site (a different observatory, home) no longer shows up in the plug list at all.
- Equipment page: an estimated Amps figure next to the measured Watts reading (e.g. "4.4 W / 0.04
  A"), using a configurable AC line voltage (Settings).
- Equipment page: per-plug consumption-alert configuration ("Max (A@12V)" and "PSU eff (%)"), for
  plugs that support energy monitoring - the rated max draw of whatever's plugged in there (e.g. a
  Pegasus Powerbox) and an estimated power-supply efficiency, set once since they're properties of the
  equipment itself.
- The "Consumption Threshold Changed" trigger and "Consumption Above/Below" loop conditions now
  target a specific plug (with its own preventive alert percentage, configurable per sequence item) -
  a single combined/summed number across every plug didn't answer the actual question ("is this
  specific piece of equipment nearing its own rated max"), since different equipment has different
  ratings.

### Changed

- Removed the "hide Tapo/newer-Kasa by default" behavior added in 0.0.0.1 - these devices are now
  fully functional, not just visible.
- Removed the global "Consumption alerts" section from Options (max threshold, preventive %, and the
  Amps-at-12V/PSU-efficiency calculator) - superseded by the per-plug equipment-page configuration
  above.
- The two consumption Loop Conditions are now labeled "Consumption Above"/"Consumption Below" (was
  "Total Consumption Above"/"Total Consumption Below") to reflect that they target one specific plug,
  not a total.

## [0.0.0.1] - 2026-07-26

### Added

- Cloud-based discovery and control of TP-Link Kasa smart plugs (on/off, LED). Control goes
  exclusively through the TP-Link cloud API - the plugin never connects to your devices over the
  local network, so it's safe to use on a shared/multi-tenant observatory network.
- Equipment page in NINA's Imaging tab: live on/off and LED status per plug, an editable "equipment"
  name per plug (e.g. "Mount", "Dew heater"), a live power reading on plugs that support energy
  monitoring, and bulk "All Plugs On/Off" and "All LEDs On/Off" buttons.
- Per-plug "Protected" lock: a protected plug can't be turned off from the sequencer at all, and
  requires two confirmations from the equipment page.
- Per-plug visibility toggle (Options page) so plugs on your TP-Link account that aren't part of
  your imaging setup can be hidden from the equipment page and sequencer.
- 8 sequencer instructions: Turn Plug On/Off (with an optional delay after turning on), Turn All
  Plugs On/Off (with the same optional delay), Turn LED On/Off, Turn All LEDs On/Off.
- 4 sequencer Loop Conditions: Plug Is On/Off, Total Consumption Below/Above a wattage threshold.
- 2 sequencer Triggers: Plug State Changed (notifies if a plug's on/off state changes unexpectedly
  during a sequence, e.g. switched from the Kasa app or a physical button by someone else) and
  Consumption Threshold Changed (notifies when total consumption crosses the preventive alert
  percentage of the configured maximum - not yet tested against real monitoring-capable hardware).
- Settings page: TP-Link account credentials (password stored encrypted on disk, masked in the UI),
  a consumption threshold and preventive alert percentage, a configurable equipment-page refresh
  interval, and an Amps-at-12V calculator (with an estimated PSU efficiency %) for entering the
  consumption threshold in Amps instead of Watts - most astro equipment (Pegasus Powerboxes, etc.)
  is rated in Amps at 12V DC, but a Kasa/Tapo plug only measures Watts on the AC side.

### Changed

- The cloud session token is now reused across refresh cycles instead of logging in fresh every
  time - only re-acquired if a call using it actually fails. Real-world reports from other TP-Link
  cloud integrations show excessive re-authentication (not moderate polling in general) is what
  triggers the API's undocumented rate limit.
- Tapo devices and newer-generation Kasa devices (e.g. KP125M) are now hidden from the equipment
  page/sequencer by default the first time they're discovered - since nothing can control them (see
  Known limitations below), leaving them visible only cluttered the plug list. Uses the existing
  per-plug "Visible in NINA" toggle (Options page), so a device already discovered before this
  change keeps whatever visibility you last set for it, and you can still manually re-enable
  visibility for one if you just want to see it listed.

### Fixed

- Errors are now written to NINA's log (in addition to the on-screen notification), so a failure's
  actual exception and stack trace aren't lost the moment the notification disappears.
- Newer-generation Kasa devices (e.g. the KP125M) use the same modern protocol family as Tapo, not
  the legacy protocol older models (HS103, KP303) use - the plugin was incorrectly attempting (and
  always failing) legacy commands against them. They're now correctly treated as not-yet-supported,
  same as Tapo, instead of erroring on every refresh.
- Newer-generation Kasa devices (same "SMART.\*" family as Tapo) showed a garbled alias like
  `S1AxMjVNLTE=` instead of their real name - TP-Link base64-encodes it, and a third-party library
  dependency only decoded it for device types it recognized as Tapo. Now decoded correctly for these
  devices too.

### Known limitations

- Only older-generation Kasa devices (using the legacy protocol - e.g. HS103, KP303) are
  controllable. Tapo devices and newer-generation Kasa devices (e.g. KP125M) use a different
  protocol (KLAP/securePassthrough) that has no cloud-relay path at all, only direct local-network
  access - they will not be controllable under this plugin's current cloud-only design. They're
  still discovered and listed (hidden by default, see above) so persisted settings aren't lost if
  local-network support is added as an opt-in in a future version.
- Consumption Threshold Changed can't be verified end-to-end yet: none of the currently-supported
  devices support energy monitoring.
