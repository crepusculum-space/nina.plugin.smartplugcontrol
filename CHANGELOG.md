# Changelog

All notable changes to Smart Plug Control are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
- 1 sequencer Trigger: Plug State Changed - notifies if a plug's on/off state changes unexpectedly
  during a sequence (e.g. switched from the Kasa app or a physical button by someone else).
- Settings page: TP-Link account credentials (password stored encrypted on disk, masked in the UI),
  a consumption threshold and preventive alert percentage (reserved for a future alert feature), and
  a configurable equipment-page refresh interval.

### Known limitations

- Tapo devices are discovered but not yet controllable - only Kasa is supported for now.
- Consumption-based alerts aren't built yet; the threshold/percentage settings above don't do
  anything on their own until that feature lands.
