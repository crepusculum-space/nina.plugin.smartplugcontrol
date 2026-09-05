# Changelog

All notable changes to Smart Plug Control are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [0.0.0.8] - 2026-09-05

### Added

- **Preliminary support for Tapo multi-outlet power strips (e.g. the P300 family/P316M).** Previously
  only a single "whole strip" entry showed up, not the individual outlets - the KLAP protocol library
  this plugin depends on for Tapo/newer-Kasa devices has no support at all for the child-outlet
  commands these strips use (`get_child_device_list`/`control_child`), so this plugin now implements
  them directly. **Not yet validated against real P300-family hardware - please report back whether
  this actually works before it's considered done.**

### Fixed

- **A Kasa legacy plug not physically reachable on this PC's local network (e.g. its WiFi is on a
  different VLAN than the client's own wired network at a multi-tenant observatory) was excluded from
  the plug list entirely, even though legacy Kasa is controlled exclusively through the TP-Link cloud
  relay and never needed local reachability in the first place.** The local-presence check now only
  applies to Tapo/newer-Kasa (KLAP) devices, which do genuinely need it. Tapo/newer-Kasa devices on a
  different network segment than the NINA PC remain unreachable - that part is a real network-topology
  limitation, not something this plugin can work around in software (see CLAUDE.md).

## [0.0.0.7] - 2026-09-03

### Fixed

- **The password field could silently fail to save on a genuinely fresh install/first-time setup**
  (as opposed to the DPAPI-failure case fixed in 0.0.0.6) - reported by the same user, still
  reproducing on two machines even with 0.0.0.6 installed. Root cause: WPF can skip invoking the
  `PasswordBox` binding helper's change callback entirely when the very first value it's asked to
  push equals that property's registered default - and an empty password (true for every user before
  they've ever saved one) used to be exactly equal to that default (`string.Empty`), so the event
  subscription that relays typed characters back to the view model never happened at all. Fixed by
  registering the default as `null` instead, which no real password value can ever equal, so the
  first bind is always seen as a genuine change.
- Wrong-password logins showed a raw, meaningless TP-Link error code
  (`"Unexpected Error Code: -20601"`) instead of a message a non-technical user could act on - now
  translated to "Invalid TP-Link username or password."
- **The equipment page's automatic poll loop retried a failing cloud login every refresh interval
  (10s by default) forever, with no backoff** - both showing a repeated error notification every
  cycle and, more seriously, hammering TP-Link's own login endpoint closely enough to trip their
  rate limiting. Confirmed with a real account: repeated failed attempts during testing of the fix
  above eventually returned a second, different error code (`-20661`) and the account showing
  "your account was locked" on tplinkcloud.com when logging in directly to check. The automatic poll
  loop now backs off for 5 minutes after a failed login before retrying on its own; an explicit
  "Refresh Plug List" click always retries immediately regardless of that cooldown.
- **That same cooldown never actually engaged as long as the login kept failing** - a fresh reinstall
  with a wrong password reproduced the exact repeated-notification-every-10s symptom the cooldown
  above was meant to prevent. Root cause: the "did credentials change" check that resets the cooldown
  compared the current username/password against the credentials of the last *successful* login -
  which, for as long as login keeps failing, never gets set, so it stays `null` forever and looks
  "changed" (i.e. different from the real, unchanging, still-wrong credentials) on every single call.
  Fixed by tracking the credentials of the last login *attempt* separately from the last success.
- **A previously-selected plug could show up on the equipment page but not in the Options page's
  plug-visibility list without an explicit "Refresh Plug List" click there.** This turned out to be a
  fundamental NINA plugin-loader constraint, not a bug we could fully engineer around: the equipment
  page and the Options page are composed via two separate MEF containers, so they never actually
  share the same in-memory plug list. Rather than doubling TP-Link API polling to paper over this,
  the Options page stays manual-refresh-only by design - documented here so it isn't mistaken for a
  regression.
- **"Turn Off All Plugs" (and other KLAP-family actions) could fail with `Forbidden`/`Bad Request`,
  or occasionally with a garbled decryption/JSON error, after otherwise working fine.** Root cause: a
  fix earlier in this same cycle made each KLAP device's local session get rebuilt from scratch on
  every automatic poll cycle (every 10s by default) instead of being reused - hammering the physical
  device's own local authentication the same way the cloud login-retry-storm above once hammered
  TP-Link's servers. The device would eventually reject the repeated handshakes outright, or (more
  confusingly) accept a stale cached session for one more request but respond with data our cached
  session could no longer correctly decrypt, surfacing as an AES padding error or malformed JSON
  instead of a clean "your session is invalid" error. Fixed by reusing a device's KLAP session across
  refresh cycles instead of rebuilding it every time, and by treating any failure of an
  already-authenticated request - not just the "named" session-expired cases - as a possibly-stale
  session worth one automatic re-login retry.

### Changed

- "Turn All Plugs On/Off" and "All LEDs On/Off" on the equipment page now update each switch
  immediately after the action succeeds, instead of waiting for the next automatic poll tick to catch
  up - matching how toggling a single plug already behaved.

### Added

- A logo (`docs/logo.png`, `FeaturedImageURL`), shown in NINA's plugin list and Options page -
  previously the plugin only showed NINA's generic placeholder icon.

## [0.0.0.6] - 2026-08-30

### Fixed

- **Entering TP-Link credentials could silently do nothing** - reported by a user: no error, nothing
  in NINA's log, no plugs ever appeared, even with an intentionally wrong password. Root cause: the
  password field's setter runs inside a WPF binding that silently swallows any exception it throws;
  if Windows' credential encryption (DPAPI) fails on a given system (e.g. a temporary/roaming Windows
  profile, or a missing crypto API on that system's .NET Windows Desktop Runtime), the password was
  never actually saved and nothing ever indicated why. Now checked proactively before every save
  attempt, with a plain-language error message and a log entry instead of silent failure.

## [0.0.0.5] - 2026-08-30

### Fixed

- **The release zip was packaged wrong and broke installs from NINA's built-in plugin repository.**
  Every prior release zip wrapped the plugin's files in a top-level
  `Crepusculum.NINA.SmartPlugControl\` folder (an artifact of zipping the raw build output
  directory). NINA's plugin manager extracts the installer archive directly into
  `<PluginsDir>\Smart Plug Control\` - it does not create an extra subfolder itself - so installing
  from the repository actually produced
  `...\Plugins\3.0.0\Smart Plug Control\Crepusculum.NINA.SmartPlugControl\*.dll`, one level too
  deep for NINA to find the DLL. A manually-copied local dev build never hit this (the dev
  `PostBuild` deploy step already targets the right depth), which is why it went unnoticed until a
  real install-from-repository was tested. Fixed by adding `scripts/package-release.ps1`, which
  zips the build output's *contents* rather than the folder itself.

## [0.0.0.4] - 2026-08-29

### Fixed

- **Sequencer triggers/conditions could silently act on stale data when the equipment page was
  hidden.** The equipment page's poll loop only refreshed `IPlugRegistryService` while its own panel
  was visible - but sequencer items read live state from that same registry regardless of whether
  the equipment page happens to be open. Refreshing is now unconditional. Found via code review
  (thanks isbeorn).
- Clarified the license identifier from ambiguous `GPL-3.0` to `GPL-3.0-only` (this project's
  `LICENSE` doesn't include the "or later version" clause).
- The `LICENSE` file is now bundled in the release zip alongside `THIRD-PARTY-NOTICES.md` - required
  for GPL-3.0 compliance, previously missing from the distributed build.

### Added

- A test project (`SmartPlugControlTests`) with regression coverage for the stale-data fix above.

## [0.0.0.3] - 2026-07-27

### Changed

- **Relicensed from MIT to GPL-3.0.** Part of `KasaCloudPassthroughClient.cs` (the legacy Kasa cloud
  passthrough protocol) was written with direct reference to two GPL-3.0-licensed projects
  (piekstra/tplink-cloud-api, python-kasa) to understand the protocol - to stay unambiguously
  compatible with their license, this plugin is now GPL-3.0 as a whole rather than MIT. See
  `THIRD-PARTY-NOTICES.md` for full credits.
- Added `THIRD-PARTY-NOTICES.md`, now bundled in every release build, crediting TapoConnect and
  BouncyCastle.Crypto (both MIT, compatible with inclusion in this GPL-3.0 project) and the two
  GPL-3.0 reference implementations above.

### Fixed

- Removed unused native runtime files (`SQLite.Interop.dll`, `sni.dll`,
  `libSystem.IO.Ports.Native.*`) that were being bundled by mistake - they came from NINA's own
  dependency tree (EntityFramework/SQLite/SqlClient/IO.Ports, used internally by NINA.Core, not by
  this plugin), leaking through despite excluding NINA.Plugin's own assembly. The distributed zip now
  contains only this plugin's own DLL plus its two real dependencies (TapoConnect, BouncyCastle).

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
