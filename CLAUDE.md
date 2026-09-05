# Smart Plug Control — NINA plugin

Controls TP-Link Kasa/Tapo smart plugs from NINA (Nighttime Imaging 'N' Astronomy) sequences.

## Who this is for and why it's built this way

This plugin is meant to run in **multi-tenant commercial remote observatories**, not just
backyard/personal setups.

## Architecture history: cloud-only → local-network-only (read this before touching control code)

This project went through two full architectural pivots. If you're only skimming, skip to "Current
architecture (as of the local-network pivot)" below - the rest of this section is *why*, kept for
context so the reasoning isn't re-litigated from scratch by a future session.

**v1 (original): local-network control**, using `Aldaviva.Kasa` and direct `http://<device-ip>`
calls.

**v2 (pivot 1, shipped as v0.0.0.1): cloud-only.** The concern: a flat/shared observatory network
without guaranteed VLAN isolation would let one client's NINA instance reach and control *any*
device on the network, regardless of which TP-Link account owns it. Routing control through
TP-Link's cloud meant TP-Link enforced the account boundary server-side, independent of network
topology. Shipped, tested on real Kasa hardware (HS103, KP303 - the legacy `IOT.*` protocol),
released on GitHub and submitted to NINA's official manifest repo
(isbeorn/nina.plugin.manifests#589 - later superseded, see below).

**v3 (pivot 2, shipped, merged into NINA's official plugin repository 2026-08-29): hybrid - cloud
discovery, protocol-routed control.** Triggered by real
hardware testing revealing Tapo and newer-generation Kasa (the `SMART.*`/KLAP protocol family, e.g.
the KP125M) have **no cloud-relay path at all** - confirmed by reading two independent community
protocol implementations (C# `TapoConnect`, and the more mature/actively-maintained Rust
`mihai-dinculescu/tapo` crate); both require a direct connection to the device's local IP with no
cloud alternative found anywhere. Cloud-only was a dead end for anything beyond legacy-protocol Kasa,
which TP-Link will likely phase out entirely over time. This reopened the security question, and a
closer look changed the answer:

- **KLAP's local handshake is credential-gated, not just network-position-gated.** Read
  `KlapDeviceClient.KlapHandshake1Async`: the device compares a hash derived from the *actual TP-Link
  account username+password* (stored on the device itself, from when it was originally paired with
  the Tapo/Kasa app) against a hash the client computes from whatever credentials it's using. A
  device reachable on a flat network by a *different* tenant's instance will simply reject the
  handshake unless that tenant happens to know the true owner's account credentials - reachability
  alone grants nothing. This is TP-Link's own fix for a documented weakness in the *older*
  "securePassthrough" Tapo protocol; KLAP was built specifically to close it. This makes local KLAP
  control **exactly as safe as cloud** for the multi-tenant threat model - the account boundary is
  enforced by the device instead of by TP-Link's servers, but it's still enforced. It also means a
  local network scan naturally filters to only the querying user's own devices (others' devices exist
  on the network but fail the handshake) - solving the "everyone has an unrenamed KP303, whose is
  whose" discovery problem for free.
- **Legacy `IOT.*` Kasa (HS103, KP303) has no local authentication at all** - true reachability-based
  risk on a flat network, this part of the original concern was and is real.
- **But a genuinely flat network at a real commercial multi-tenant observatory is implausible in
  practice**, independent of this plugin entirely: such a site would also expose every client's NINA
  PC (RDP/VNC), ASIAir/Stellarvita units, IP cameras, and any WiFi-equipped mount or Pegasus flat
  panel to every other client. An operator running that way has business-ending problems this plugin
  neither causes nor could fix, and it's hard to imagine a paying customer accepting the arrangement.
  This is a *probabilistic real-world argument*, not a protocol guarantee like the KLAP point above -
  worth keeping that distinction in mind if this gets re-litigated later.

Given both points, the user initially leaned toward dropping cloud control entirely and rewriting
both device families on local-network control. **That was then superseded by a simpler hybrid the
user proposed directly**, adopted as the final design (see "Current architecture" below): keep
cloud discovery (the account is already the authoritative device list - the user must add a plug to
their Tapo/Kasa account to use it at all, so no network scan is needed to *find* devices), add a
local-presence check via ARP to filter the cloud's device list down to only what's physically on
this PC's network (fixes the "my Texas observatory turns on my home stereo at 3am" cross-site risk
as a bonus), and then route control by protocol rather than by user choice: KLAP-family devices
control locally (safe per the point above), legacy Kasa still controls via cloud (since legacy has
no local auth at all, even a confirmed-local device still needs the account-boundary check only the
cloud can provide). Do not reintroduce a "cloud mode" toggle or an all-local design without this
reasoning being explicitly revisited with the user first - this was a deliberate, considered choice,
not an oversight.

## Current architecture (as of the local-network pivot)

Implemented as a **hybrid**: discovery stays 100% cloud; a local-presence check filters the result to
this site only; control is then routed per-device by protocol, not by any user-facing setting.

- **Discovery**: unchanged - `TpLinkCloudClient.ListDevicesAsync` (`SmartPlugControlCloud/`) is still
  the only source of the device list. The account is authoritative; nothing scans the network to
  *find* unknown devices.
- **Local presence filter**: `SmartPlugControlServices/LocalPresenceResolver.cs`. Once per
  `RefreshAsync` cycle, `WarmArpCacheAsync` enumerates this PC's active IPv4 interfaces/subnets and
  pings every host in each (capped at /24-sized subnets) purely to force the OS to populate its ARP
  table - even devices that never answer ICMP still trigger the underlying ARP resolution needed to
  attempt routing to them. `PlugRegistryService.RefreshAsync` then resolves each cloud device's known
  `DeviceMac` against that ARP cache via `TapoConnect.Util.TapoUtils.TryGetIpAddressByMacAddress`
  (already a project dependency, reads `arp -a` - no ARP parsing was hand-rolled). A device whose MAC
  can't be resolved locally is **excluded entirely** from both `Plugs` and `AllPlugs` - not hidden,
  gone - since it isn't physically at this site.
  **Update: this exclusion now applies only to `SMART.*`/KLAP devices, not `IOT.*`/legacy Kasa ones -
  see the "Multi-tenant observatory VLAN limitation" section below for why.**
- **Control routing** (`PlugRegistryService.RefreshAsync`, by `DeviceType` prefix):
  - `IOT.*` (legacy Kasa, e.g. HS103/KP303) → unchanged cloud-relay path
    (`KasaCloudPlugDriverFactory`/`KasaCloudPlugDriver`/`KasaCloudPassthroughClient`).
  - `SMART.*` (Tapo and newer-generation Kasa, e.g. KP125M) → new
    `SmartPlugControlDrivers/KlapPlugDriver.cs`, which wraps `TapoConnect.TapoDeviceClient`
    (`LoginByIpAsync`/`GetDeviceInfoAsync`/`SetPowerAsync`/`GetEnergyUsageAsync`) pointed at the
    ARP-resolved local IP, using the same TP-Link account credentials already stored for cloud login.
    A KLAP login/read failure (e.g. a hypothetical mismatched-account edge case) is logged and the
    device is excluded that cycle, same as any other driver failure. `SupportsLedAsync` currently
    always returns `false` - no LED field was found in `DeviceGetInfoResult` as of TapoConnect 3.2.4;
    re-verify against real P115/KP125M JSON if this matters later.
- `IPlugDriver`/`IPlugRegistryService`/`PlugViewModel`/`PlugPersistedData` and every sequencer
  item/the equipment page are all unchanged - they only ever talk to `IPlugRegistryService`, which
  hides all of the above behind the same interface it always had.

**Verified against real hardware (2026-07-27)**: 2x HS103 + KP303 legacy (unchanged, still cloud),
2x Kasa KP125M (new, KLAP local control - worked immediately), Tapo P115 (new, KLAP local control -
needed one extra step, see gotcha below). Still to test: the "wrong TP-Link password → KLAP device
excluded, not crashed" scenario that validates the core security argument for this whole pivot.

## Current status

**Done and verified working inside a real running NINA instance** (not just compiled):
- Cloud discovery, on/off, LED control for Kasa (HS103 x2, KP303 power strip - real hardware).
- KLAP local-network control for Tapo/newer-Kasa (`KlapPlugDriver`) - verified against real hardware
  (Tapo P115, 2x Kasa KP125M) - see "Verified against real hardware" note under "Current
  architecture" above.
- Per-plug "visible in NINA" toggle (Options page) - the TP-Link account may have devices unrelated
  to the observatory (a home TV, a printer); hidden plugs are excluded everywhere, including bulk
  actions (`TurnOnAllAsync`/`TurnOffAllAsync`/`SetAllLedsAsync`).
- Equipment page (dockable panel) in NINA's **Imaging** tab. This placement is a **deliberate, final
  decision** (the user considered the Equipment tab / Installed Plugins settings, chose Imaging
  because the toolbar icon there lets it be shown/hidden without leaving the Imaging view) - don't
  relocate without being asked again.
- `PlugControlDockableVM`'s poll loop refreshes `IPlugRegistryService` unconditionally now, not just
  while the dock panel is visible (found in PR review - hiding the equipment page used to silently
  freeze the data sequencer triggers/conditions read). Covered by a new `SmartPlugControlTests`
  project (xUnit, hand-written fakes for `IPlugRegistryService`/`IProfileService` - no Moq/mocking
  library added, to keep the test-only dependency surface minimal) - run with
  `dotnet test SmartPlugControlTests/SmartPlugControlTests.csproj`. This is the only project with
  automated tests; everything else in this plugin is still verified manually inside real NINA (see
  the gotcha below about why `dotnet build` alone can't prove UI-level correctness).
- 8 sequencer Instructions (Turn On/Off Plug with post-on delay, Turn On/Off All Plugs, Turn On/Off
  LED, Turn All LEDs On/Off) - "Turn Off Plug" hard-blocks via `IValidatable` on a protected plug.
- 4 sequencer Conditions (Plug Is On/Off, Total Consumption Below/Above) - these show up under
  **"Loop Conditions"** in NINA's Advanced Sequencer, not a generic "Conditions" section; NINA doesn't
  group that dropdown by category the way it does Instructions (confirmed in NINA's own source,
  `SequencerFactory.cs` - `ConditionsView` has no `GroupDescriptions`), so no per-plugin header shows,
  it's just a flat sorted list.
- 1 sequencer Trigger (Plug State Changed) - notifies if a plug's state changes without the sequence
  having caused it. Deliberately does **not** try to auto-restore the previous state - matches the
  project's stance that a protected/critical plug's power state is a human decision.

**Deliberately shelved** (revisit only if the user brings them up again - don't build proactively):
- **Groups/Locations** (the user rents space across multiple physical observatories, e.g. 2 spaces at
  "AstroPeak" + 1 at "Starfront", and wanted to show/hide and bulk on/off by group/location like the
  Kasa app's own grouping). No evidence the TP-Link cloud API exposes group/location membership as a
  readable field. If revisited: don't depend on Kasa's own groups - replicate with our own free-text
  `Group`/`Location` tags on `PlugPersistedData`, and iterate our own tagged-plug list for bulk actions
  (same mechanism as `TurnOnAllAsync`, just filtered) rather than reverse-engineering another
  TP-Link endpoint. Shelved because the Imaging-page UI redesign this implies needs real thought, not
  because of a technical blocker.
- **Consumption-triggered corrective action** (e.g. "if a Pegasus Powerbox's amp draw on one Kasa
  plug nears its rated limit, turn off a specific downstream port/dew-heater"). Doesn't work: a Kasa
  plug only meters/switches its own whole circuit, it has zero visibility into what's plugged into a
  Pegasus Powerbox downstream of it - the only real action available is turning off a *different whole
  plug* (load-shedding between independent circuits), which didn't match the user's actual use case.
  A simpler *notification-only* consumption-threshold trigger (no control action) might still be worth
  building later.
- **Tapo/newer-Kasa support (KLAP protocol): SUPERSEDED - see "Architecture history" section above.**
  This bullet originally recorded a decision to permanently exclude Tapo/newer-Kasa and stay
  cloud-only; that decision was reversed shortly after when closer analysis of KLAP's credential-gated
  handshake (plus the "a real flat multi-tenant network is implausible anyway" argument) led the user
  to drop cloud entirely and go local-network-only for everything instead. Kept below for the still-useful
  technical details (real `DeviceType` values, protocol research) - don't take the "permanently
  exclude"/"never build a driver" framing at face value, it's the *previous* decision, not the current one.
  Hardware arrived (Tapo P115, Kasa KP125M) and testing resolved the open question from earlier in this
  project - real `DeviceType` values logged from the live account: `HS103`/`KP303` → `IOT.SMARTPLUGSWITCH`
  (legacy, what `KasaCloudPassthroughClient` implements); `KP125M` (a **Kasa-branded** device) →
  `SMART.KASAPLUG`; `P115` (Tapo) → `SMART.TAPOPLUG`. TP-Link migrated newer Kasa models to the same
  `SMART.*`/KLAP-based protocol family Tapo uses, regardless of brand. `PlugRegistryService.RefreshAsync`
  gates on `DeviceType` starting with `"IOT."` (not `Brand != PlugBrand.Kasa`) precisely because of this -
  a `SMART.KASAPLUG` device is still brand=Kasa but can't be controlled via the legacy passthrough
  protocol, so it falls into the same "unsupported, list only" branch as Tapo instead of attempting (and
  always failing) legacy commands against it.
  **The real finding: KLAP/securePassthrough has no cloud-relay path at all.** Read both protocol
  implementations in `TapoConnect` (`Protocol/KlapDeviceClient.cs`, `Protocol/SecurePassthroughDeviceClient.cs`)
  - every request (`handshake1`/`handshake2`/`request` for KLAP; `handshake`/`securePassthrough` for the
  older Tapo protocol) is a raw HTTP POST straight to `http://{deviceIp}/...`, performing a cryptographic
  handshake with the device's own firmware directly. Unlike the legacy Kasa protocol (which posts to a
  cloud `appServerUrl` that TP-Link's own infrastructure relays to the device), there is no cloud
  endpoint anywhere in this library - or found elsewhere - that proxies KLAP. This is exactly the
  finding that triggered the v3 pivot (see "Architecture history" above) - `SMART.*` devices now have
  a real driver (`KlapPlugDriver`, local-network control) instead of being list-only.

- **Alias for `SMART.*`-family devices is base64-encoded by TP-Link's cloud - confirmed by adding a
  second KP125M via the Kasa app (not Tapo) specifically to rule out "does the pairing app matter?"
  (it doesn't - `DeviceType` reflects the device's own firmware, not which app onboarded it).** Both
  KP125M devices showed a garbled `Alias` like `S1AxMjVNLTE=` in NINA, which decodes via base64 to the
  device's real name (`KP125M-1`). Root cause: `TapoConnect.TapoCloudClient.ListDevicesAsync` only
  base64-decodes `Alias` for `DeviceType`s `TapoUtils.IsTapoDevice` recognizes (the same narrow
  3-string whitelist from the brand-classification gotcha above) - so a `SMART.KASAPLUG` device's
  alias comes back still-encoded, while `SMART.TAPOPLUG` (actual Tapo, e.g. the P115) gets decoded
  correctly by the library itself. Fixed in our own `TpLinkCloudClient.ListDevicesAsync`
  (`DecodeAliasIfNeeded`) rather than patching the third-party package: for any `DeviceType` starting
  with `"SMART."` that the library's own check didn't already decode, base64-decode it ourselves
  (`TapoCrypto.Base64Decode`, wrapped in try/catch for `FormatException` in case a future `SMART.*`
  type turns out not to be encoded after all). Legacy `IOT.*` devices were never affected - their
  alias was always plain text.

**Phase 6 (consumption alerts) - code done, redesigned per-plug (2026-07-27, see below).** Added
`ConsumptionThresholdTrigger` (`SmartPlugControlSequenceItems/ConsumptionThresholdTrigger.cs`), the
`TotalConsumptionAboveCondition`/`TotalConsumptionBelowCondition` Loop Conditions, and a near-exact
copy of `PlugStateChangedTrigger`'s baseline/transition/notify-only pattern for the trigger
(`ShouldTrigger`/`ShouldTriggerAfter` both overridden, same instruction-boundary reason). Energy
monitoring itself is confirmed working against real hardware: the Tapo P115 (2026-07-27) reported
4.4W via `KlapPlugDriver.GetPowerAsync`, matching the Tapo app's own reading exactly - confirms the
`CurrentPower / 1000.0` milliwatt-to-watt scaling assumption was correct - **though noticeably
noisy**: the same P115, with a constant load (a Hue LED bulb) plugged in, reported anywhere from 4.4W
to 4.7W across successive refreshes. Confirmed with the user that this is expected/acceptable - the
whole point of these triggers/conditions is a rough "is consumption roughly where expected" signal
for a human-facing notification, not a precise measurement to build tighter automation logic around.
**All three consumption-related sequence items must stay notification-only, never a corrective
action** - documented prominently in the README's Triggers section for anyone using this plugin, not
just as an internal note. See also the already-shelved "Consumption-triggered corrective action" idea
below for why an automatic response wouldn't even be technically sound in the first place (no
visibility into what's downstream of a given plug).

**Per-plug redesign (2026-07-27) - the original "total across all plugs" design was wrong.** The
trigger/conditions originally summed consumption across every monitored plug against one global
threshold (`Settings.Default.MaxConsumptionThresholdWatts`/`PreventiveAlertPercent`, entered in
Options, optionally computed from an Amps-at-12V + PSU-efficiency calculator - all since removed, see
below). The user corrected this during real testing: **a single global/summed threshold is close to
useless in practice** - their actual use case is "I have an 8A-rated Powerbox on one plug and a
12A-rated one on another; I want to know when *each specific plug* nears *its own* rating," and
summing everything together, or comparing every plug against the same number, doesn't answer that
question at all. (Earlier in this same conversation I'd also mischaracterized per-socket monitoring
itself as a Kasa/Tapo hardware limitation - it isn't; `PlugViewModel.LastPower` already exists
per-socket. The real limitation, unchanged, is that a plug can't see *inside* whatever's plugged into
it - e.g. which port of a Pegasus Powerbox is drawing current - not that it can't report its own
total draw.) Final design, arrived at through several rounds of back-and-forth:
- **Max threshold config (MaxAmpsAt12V, PsuEfficiencyPercent) moved to `PlugPersistedData`,
  configured per-plug on the equipment page** (`PlugControlDockableTemplates.xaml`'s "Max (A@12V)"/
  "PSU eff (%)" columns, only shown when `PlugViewModel.SupportsPowerMonitoring` is true). Rationale:
  these are essentially invariant properties of whatever's plugged in there (a Powerbox's rated max
  doesn't change), so they're a "set once and forget" equipment property, naturally colocated with
  the Alias/Equipment-name/live-reading columns already there - not something to re-enter in every
  sequence that references the plug.
  `PlugViewModel.MaxThresholdWatts` computes `(MaxAmpsAt12V × 12V) / (PsuEfficiencyPercent / 100)`,
  null if not configured (`MaxAmpsAt12V == 0`).
- **The plug picker AND the preventive alert % stayed per-sequence-item** (on
  `ConsumptionThresholdTrigger` and both Loop Conditions) - deliberately, per the user: different
  sections of the same sequence may want different tolerance for the *same* plug (e.g. more tolerant
  during a brief flats routine with a light panel drawing extra current on the same circuit). This is
  the one piece of "per-instance re-entry" that's actually wanted, not accidental duplication.
  `ConsumptionThresholdTrigger` now extends `PlugTriggerBase` (reusing its plug-picker/validation
  machinery, same as `PlugStateChangedTrigger`) instead of bare `SequenceTrigger`.
  `ConsumptionThresholdConditionBase` gained the same `SelectedPlugId`/`AvailablePlugs`/`SelectedPlug`
  shape (previously only `ThresholdWatts`) plus `IValidatable.Validate()` requiring both a plug
  selected AND that plug having a configured `MaxThresholdWatts` (points the user at the equipment
  page if not). The two condition classes keep their original C# names
  (`TotalConsumptionAboveCondition`/`TotalConsumptionBelowCondition`) to avoid breaking
  already-in-progress saved sequences (NINA serializes sequence items by .NET type name) - only their
  `[ExportMetadata("Name", ...)]` *display* names changed (harmless, not part of serialization) to
  "Consumption Above"/"Consumption Below" now that they're per-plug, not a sum.
- **Notification/comparison math stays in Watts internally (that's all a plug can measure), but the
  user never sees Watts** - not as input, not in the notification message. The preventive Amps figure
  shown to the user is computed directly as `MaxAmpsAt12V × PreventiveAlertPercent / 100` (no Watts
  round-trip needed - the PSU efficiency estimate cancels out exactly in that particular ratio, proven
  algebraically during the design discussion). The *current* reading has no such ratio to exploit
  (it's an absolute measurement, not a fraction of the max), so it does need the full conversion:
  `currentWatts × (PsuEfficiencyPercent / 100) / 12V` to get an equivalent estimated DC-side Amps
  figure for display.
- **Removed entirely**: the global Options "Consumption alerts" section (`MaxConsumptionThresholdWatts`/
  `PreventiveAlertPercent`/`MaxConsumptionThresholdAmps`/`PsuEfficiencyPercent` bindable properties on
  `SmartPlugControl.cs`, the matching `Settings.settings`/`Settings.Designer.cs` entries, and
  `RecomputeWattsFromAmps()`) - fully superseded by the per-plug equipment-page fields above.

**Not started**: community-facing docs for adding other brands.

## Tapo multi-outlet power strip support (P300 family/P316M) - implemented, NOT yet validated on real hardware

Two real user reports surfaced this: a P316M user at home saw only one entry for the whole strip
instead of each outlet, and a P300 user at a remote observatory saw nothing at all (a separate,
network-topology issue - see the VLAN section below). Root cause for the multi-outlet symptom:
`TapoConnect`'s `DeviceGetInfoResult` DTO has no children/child-device field of any kind - confirmed by
reading it directly - so `KlapPlugDriver` always treated a KLAP-family device as exactly one outlet,
correct for a real single-outlet plug (P115, KP125M) but wrong for a power strip.

The real KLAP protocol *does* support child outlets (confirmed three ways: reading python-kasa's
`kasa/smart/modules/childdevice.py`/`smartchilddevice.py`/`protocols/smartprotocol.py`; a real P300
capture in python-kasa's own test fixtures, `tests/fixtures/smart/P300(EU)_*.json`; and the P316M user
independently confirming success with `python-kasa`'s own CLI, using `--child`/`--child-index` against
their real hardware) via two commands TapoConnect never implemented: `get_child_device_list` (returns
`child_device_list`, an array where each entry has the exact same shape as a normal
`DeviceGetInfoResult` - reused as-is) and `control_child` (wraps a normal single-device command, e.g.
`set_device_info`, inside `{"control_child": {"device_id": "<child>", "requestData": {...}}}`, sent on
the same already-established KLAP session as the parent - no extra per-child login).

**License note**: python-kasa is GPL-3.0-or-later (confirmed by reading its `LICENSE` directly) -
combining GPL-3.0-or-later code into this plugin's own GPL-3.0-only license costs nothing extra, since
that relicense already happened for a different reason (see the GPL-3.0 relicense section below) -
unlike the Rust `mihai-dinculescu/tapo` crate (MIT) considered earlier for this same problem, which
would have needed language-porting effort with no shared code path.

**Implementation** (all new, nothing existing was rewritten):
- `SmartPlugControlDrivers/PowerStripKlapDeviceClient.cs` - subclasses TapoConnect's own
  `KlapDeviceClient`, adding `GetChildDeviceListAsync`/`SetChildPowerAsync`. Built entirely on
  TapoConnect's own `protected virtual KlapRequestAsync<TResponse>` (confirmed accessible from a
  subclass in a different assembly) - the handshake/session/cipher are reused completely unchanged,
  only the two new request/response DTOs are new (`ChildDeviceListResponse`/`ControlChildResponse`,
  built from TapoConnect's own public `TapoRequest<T>`/`TapoResponse<T>`/`TapoSetBulbState`/
  `DeviceGetInfoResult` types - no private/internal TapoConnect types were needed at all).
- `KlapPlugDriver.cs` - gained an optional `childDeviceId` constructor parameter; when set, every
  operation (`IsOnAsync`/`TurnOnAsync`/`TurnOffAsync`) is routed through the new child-aware calls
  instead of the plain single-device ones. Per-child energy monitoring is assumed unsupported
  (`GetPowerAsync` returns null for a child driver) since a power strip's outlets don't meter
  themselves individually on Kasa's equivalent hardware (KP303) either - unverified against a real
  Tapo strip.
- `KlapPlugDriverFactory.cs` (new, mirrors the long-existing `KasaCloudPlugDriverFactory` for legacy
  Kasa power strips) - probes every KLAP device once per refresh cycle via
  `GetChildDeviceListAsync`; if it comes back empty, returns the single ordinary driver unchanged; if
  not, returns one driver per child outlet (`PlugId` format `"{deviceId}:{childDeviceId}"`, matching
  the legacy Kasa convention), all sharing the parent's already-established KLAP session
  (`KlapPlugDriver.AdoptSessionFrom`) rather than logging in once per outlet.
- `PlugRegistryService.RefreshAsync`'s KLAP branch now calls the factory and loops over however many
  drivers it returns, mirroring the existing legacy-Kasa loop's per-driver try/catch (one outlet
  erroring doesn't take down the rest of the strip).

**What's genuinely unverified**: no P300-family hardware was available to test against - the two
users mentioned above (P316M at home, P300 at a remote observatory) are the only realistic path to
validating this before it ships. Don't advertise this as fixed anywhere user-facing (README, changelog,
release notes) until at least one of them confirms it actually works.

## Multi-tenant observatory VLAN limitation - a real, mostly-unfixable gap in the core premise

A user pointed out something the whole "hybrid" architecture (see "Architecture history" above) never
accounted for: a real commercial multi-tenant observatory typically gives each client's Ethernet-wired
NINA PC its own isolated VLAN, while smart plugs sit on the site's own shared WiFi - a **different**
VLAN. This showed up as a real bug report: a P300 user at exactly such a site saw nothing at all in
the plug list, not even an error.

**Root cause, confirmed by reading the code**: `LocalPresenceResolver.WarmArpCacheAsync` only ever
pings addresses within *this PC's own interfaces' subnets* - it has no way to even attempt reaching a
device on a different VLAN, regardless of whether routing between them would otherwise be possible.
`PlugRegistryService.RefreshAsync` then excluded **every** device whose MAC couldn't be ARP-resolved
this way - including legacy `IOT.*` (cloud-relay) devices, which never actually needed local
reachability at all for control (only KLAP devices structurally need it, to know which IP to connect
to).

**What's fixable vs. not**:
- **Legacy Kasa (`IOT.*`)**: fully fixable, and fixed - the local-presence requirement for these was
  never a security boundary (TP-Link's own cloud login already guarantees the account boundary
  regardless of network topology), just a convenience filter to avoid showing a device from a
  *different* one of the user's own sites (e.g. home vs. observatory, same account). `RefreshAsync`'s
  device loop now only applies the local-presence exclusion when `!usesLegacyProtocol` - legacy Kasa
  devices are shown/controlled unconditionally, same as before this whole VLAN issue was discovered.
  "Visible in NINA" remains the manual way to hide a genuinely-unrelated device, replacing what the
  automatic ARP heuristic used to (unreliably) do for this case.
- **KLAP (`SMART.*`, Tapo/newer Kasa)**: **not fixable in software** - these devices control directly
  over the local network, so if the plug's VLAN and the PC's VLAN aren't the same broadcast domain (or
  routed together, which the observatory's own network isolation is presumably specifically designed
  to prevent), there is no IP this plugin could ever reach it at. This is a real, hard limitation of
  the multi-tenant-with-per-client-VLANs deployment pattern specifically - the actual fix is
  operational, not code: whoever runs the site needs to put a client's Tapo/newer-Kasa smart plugs on
  that **same client's** VLAN, not a shared site-wide WiFi network. Not yet documented anywhere
  user-facing (README) - should be, once the legacy-Kasa fix above is confirmed with real users.

## Settings page (Phase 7, done)

Real settings now exist (`Options.xaml` + bindable properties on the `SmartPlugControl` manifest
class, all persisted via `Settings.Default`): TP-Link account credentials, a configurable
equipment-page refresh interval (was hardcoded to 10s, now `PlugControlDockableVM`'s poll loop reads
`Settings.Default.RefreshIntervalSeconds` fresh every cycle), and `LineVoltage` (display-only, see
Phase 6 above for why the consumption-alert thresholds themselves ended up per-plug on the equipment
page instead of here).

**"Turn LEDs off at sequence start / on at sequence finish" was attempted and removed - it never
actually worked (LEDs stayed on) and the real cause was never found, so don't rebuild this without
diagnosing that first.** It required subscribing to `ISequenceMediator.SequenceStarting`/`SequenceFinished`
(only available after bumping `NINA.Plugin` from `3.0.0.2017-beta` to `3.2.0.9001` at the time - since
reverted, see "NINA.Plugin package version" below). Subscribing directly in the plugin constructor
crashed the whole plugin load with a `NullReferenceException` **thrown inside NINA's own code**
(`SequenceMediator.add_SequenceStarting`) - confirmed by fetching NINA's actual source at the
`Version-3.2` tag: the event accessors (and even the `Initialized` getter) unconditionally
dereference a `sequenceNavigation` field that stays null until NINA's own Sequencer UI calls
`RegisterSequenceNavigation()`, which happens well after plugins are composed at startup. A
background retry loop (poll every 2s, catch and retry until the subscription succeeds) fixed the
crash. The user then reported what looked like interference with sequence execution (a `Turn Off
Plug` instruction only firing once) - that specific symptom turned out to be user error (a sequence
that needed a manual reset between runs), not caused by our code. **But separately, the LED
automation itself never actually worked - the LEDs stayed on regardless**, so this wasn't just a
false alarm to dismiss; the feature was genuinely broken and its root cause (why `SetAllLedsAsync`
never took effect once the subscription did succeed) was never diagnosed. The user chose to drop it
rather than keep debugging: an explicit "Turn On/Off LED" sequencer instruction at the start of the
sequence (already built, see the 8 Instructions above) does the same job and is known to work.
Feature fully removed: no
`ISequenceMediator` dependency, no `SubscribeToSequenceEventsWithRetryAsync`, no
`LedsOffAtSequenceStart`/`LedsOnAtSequenceFinish` settings/checkboxes. If the user asks for this
again, the NINA-side subscription-timing fix above is known-good (it did stop the crash), but that's
only half the problem - the actual `SetAllLedsAsync(false)`/`SetAllLedsAsync(true)` calls inside the
event handlers never visibly toggled the LEDs, and why is still unexplained. Debug that before
re-adding: check whether the handlers were actually invoked at all (add logging), not just whether
subscribing succeeded.

## NINA.Plugin package version (reverted back to 3.0.0.2017-beta)

`SmartPlugControl.csproj`'s `NINA.Plugin` PackageReference was bumped to `3.2.0.9001` mid-project
specifically for `ISequenceMediator.SequenceStarting`/`SequenceFinished` (the LED-at-sequence
feature). That feature was later removed entirely (see above) without ever reverting the package
version - meaning the assembly manifest's `MinimumApplicationVersion` (`Properties/AssemblyInfo.cs`,
still `"3.0.0.2017"`, used by NINA's plugin manager/repository to tell OTHER users' installs whether
they're compatible) had drifted out of sync with what we actually compiled against. Once nothing in
the codebase needed a 3.2-only API anymore, this was reverted back to `3.0.0.2017-beta` - verified by
a clean rebuild (0 compile errors, deployed fine) after the change. Keep the package version and
`MinimumApplicationVersion` honestly in sync going forward: if a future feature genuinely needs a
newer NINA API, bump both together and document why (like the LED feature did); if that feature is
later removed, revert the package version too, don't leave it bumped "just in case".

**Resolved: the SQLite/SqlClient/IO.Ports native bloat was NOT from TapoConnect - it was NINA.Core's
own dependency graph leaking through, and is now actually fixed (2026-07-27).** Earlier notes here
guessed the `SQLite.Interop.dll`/`sni.dll`/`libSystem.IO.Ports.Native.*` files in the build output
were "likely a transitive dependency from TapoConnect" - checked TapoConnect's own `.nuspec` directly
and that's wrong: it depends on nothing but `Portable.BouncyCastle`. The real source was NINA.Core
(pulled in transitively via `NINA.Plugin`), which uses EntityFramework/SQLite/SqlClient/IO.Ports
internally for its own purposes unrelated to this plugin. `ExcludeAssets="runtime"` on the
`NINA.Plugin` reference only excluded NINA.Plugin's own managed assembly - "native" is a *separate*
NuGet asset category and doesn't get excluded by `runtime` alone, so `CopyLocalLockFileAssemblies`
kept copying those native interop binaries from further down NINA.Core's graph even though we never
use any of them. Fixed by changing the exclusion to `ExcludeAssets="runtime;native"` - confirmed via a
clean rebuild (deleted `bin`/`obj` first) that the output now contains only `TapoConnect.dll` and
`BouncyCastle.Crypto.dll` (TapoConnect's one real dependency) alongside our own assembly - nothing
else. This matters beyond just bloat: it also narrows the actual third-party-license surface of what
this plugin redistributes down to exactly those two (both MIT), rather than the wider set assumed
before this was investigated.

## How TP-Link cloud control actually works (reverse-engineered, not officially documented)

No .NET library implements device *control* via the cloud (only TapoConnect's `TapoCloudClient` does
*discovery* — login + `getDeviceList` — which is what `TpLinkCloudClient` wraps). Control was built
from scratch in `KasaCloudPassthroughClient`, based on reading `piekstra/tplink-cloud-api`'s Python
source (not just its docs, which were misleading in places — see gotchas below):

- Each device from `getDeviceList` has its own `appServerUrl`. Commands are POSTed there as
  `{"method":"passthrough","params":{"deviceId":...,"requestData":"<json>"}}`, signed with HMAC-SHA1
  using Kasa's public app-identifying (not user-specific) AccessKey/SecretKey.
- `requestData` is the same JSON the legacy local Kasa protocol uses, e.g.
  `{"system":{"set_relay_state":{"state":1}}}`.
- Power strips (HS300/KP303) address an individual socket via `"context":{"child_ids":[childId]}`
  alongside the command. `childId`s come from `system.get_sysinfo`'s `children[]` array.
- **The `led_off` field only exists at the parent/device level, not per child.** A KP303 has exactly
  one physical indicator LED for the whole strip, not one per socket — confirmed by dumping raw
  `get_sysinfo` JSON from real hardware. `KasaCloudPlugDriver` always reads/writes LED state on the
  parent device (`childId: null`) regardless of which socket the driver instance represents.

## GPL-3.0 relicense (2026-07-27) — read this before touching `KasaCloudPassthroughClient.cs`

This plugin was MIT-licensed through v0.0.0.2. It is now **GPL-3.0** (see repo root `LICENSE`), and
this was a deliberate, informed decision — don't revert to MIT without this reasoning being
explicitly revisited with the user first.

**Why**: while auditing all third-party sources this project ever consulted, the user asked whether
proper license attribution had been done. That surfaced a real question about
`KasaCloudPassthroughClient.cs` (see "How TP-Link cloud control actually works" above) - it was
written by directly reading two GPL-3.0-licensed reference implementations
(`piekstra/tplink-cloud-api`'s `signing.py`/`device_client.py`/`device_manager.py`, and
`python-kasa` for the `set_led_off` command-shape fix), not from independent protocol observation
(e.g. capturing real network traffic between the official Kasa app and TP-Link's servers). A direct
side-by-side comparison confirmed the resulting C# is structurally very close to the Python source -
same variable-for-variable sequence in the HMAC-SHA1 signing logic, same literal `X-Authorization`
header format, same `AccessKey`/`SecretKey` constant values. Some of this (the exact signing string
format, the app-identifying key values) is arguably dictated by TP-Link's actual protocol or is
non-copyrightable fact/data rather than creative expression, but enough of the file's overall
organization was judged too close to a plausible "derivative work" of GPL-3.0 sources to respond to
this any other way than assuming that it is one.

**Why GPL-3.0 for the *whole* plugin, not just that one file**: the plugin compiles into a single
`Crepusculum.NINA.SmartPlugControl.dll` - there is no way to ship one file under GPL-3.0 and the rest
under MIT within a single compiled/combined program (per the FSF's own interpretation, a program that
links/combines GPL-3.0 code into one binary is a "combined work" that must be GPL-3.0 as a whole when
distributed - this isn't "mere aggregation" of independent programs, which is the one case GPL-3.0
doesn't require this for).

**Why not just get this file rewritten clean-room instead**: a legitimate clean-room rewrite requires
someone who has *never seen* the original GPL-3.0 source to write the replacement, working only from
an independently-derived behavioral spec (e.g. from capturing real network traffic, not from reading
piekstra's/python-kasa's code). Since this Claude Code session had already read both GPL-3.0 sources
directly (to do the side-by-side comparison above), it was disqualified from doing that rewrite
itself in the same conversation - doing so would not be a legitimate clean-room process, just
cosmetic paraphrasing of something already read, which wouldn't hold up if ever scrutinized. A real
clean-room redo (capture actual Kasa/TP-Link app traffic with a proxy, write a spec from that alone,
have an implementer who never read piekstra/python-kasa write from the spec) remains a theoretical
future option if MIT is ever wanted back, but wasn't pursued here.

**Why not get a lawyer's opinion instead**: explicitly ruled out by the user - not worth paying for
legal advice on a plugin being given away for free. GPL-3.0 was chosen specifically because it's a
free, zero-cost way to be unambiguously compliant without needing that judgment call resolved.

**Confirmed no new conflicts from this choice** (audited against every third-party source this
project has ever touched, see "How TP-Link cloud control actually works" above and
`THIRD-PARTY-NOTICES.md`):
- `TapoConnect`/`Portable.BouncyCastle` (both MIT, the only two things actually bundled in the
  distributed zip - see the bloat-removal note below) - MIT is one-way compatible with GPL-3.0
  (permissive code can be included in a copyleft work; the reverse isn't true, which was the whole
  problem being solved here).
- `NINA`/`NINA.Plugin` (host, MPL-2.0) - never redistributed by this plugin
  (`ExcludeAssets="runtime;native"`), so its license doesn't constrain this plugin's own license
  choice at all. Confirmed via real precedent too: **Touch-N-Stars, a plugin already installed in the
  user's own NINA instance, is itself GPL-3.0** - multiple other published NINA plugins are GPL-3.0
  too (Point3D, CollimationHelperForSkyWave, Subframes, GuidingAnalyzer - checked directly against
  `isbeorn/nina.plugin.manifests`). NINA's manifest schema's `License` field is free text, not a
  restricted enum - no plugin-ecosystem obstacle to GPL-3.0 either.
- `mihai-dinculescu/tapo` (Rust, read for research only, never shipped or copied) - irrelevant
  regardless of this plugin's own license.
- `Aldaviva/Kasa` (Apache-2.0, used in the long-superseded v1 local-network code, fully removed from
  the current codebase) - not present in current source, and Apache-2.0 is explicitly GPL-3.0
  compatible anyway (unlike GPL-2.0) even if it were.

**Practical consequence the user explicitly confirmed understanding before choosing this**: GPL-3.0
is *not* "no different from MIT" - it's the opposite of permissive-license freedom. Anyone who
distributes a modified version of this plugin (or incorporates it into another program) must
distribute that too under GPL-3.0 and provide corresponding source - they can no longer fold it into
a closed-source product the way MIT would have allowed. For an end user who just installs the plugin
in NINA, nothing changes.

**PR review feedback from isbeorn (2026-08-10) - addressed in v0.0.0.4**: submitting the manifest PR
surfaced two more concrete gaps, worth remembering:
- **`GPL-3.0` alone is an ambiguous SPDX identifier** - GPL-3.0-only and GPL-3.0-or-later are
  different licenses (whether downstream users may relicense under a hypothetical future GPL
  version). This project's `LICENSE` file's applied notice (the "How These Terms Apply to This
  Program" section at the bottom, which replaced the FSF's unfilled boilerplate placeholder) commits
  to `GPL-3.0-only` specifically - no "or later version" clause. Kept consistent across
  `AssemblyInfo.cs`, `README.md`, and the NINA manifest. `THIRD-PARTY-NOTICES.md` also now notes that
  python-kasa's own `LICENSE` explicitly says "or later" (so it's GPL-3.0-or-later) while piekstra's
  `LICENSE` leaves the same clause unfilled (genuinely ambiguous on their end too).
- **The `LICENSE` file itself wasn't being bundled in the release zip** - only
  `THIRD-PARTY-NOTICES.md` was wired to copy to the build output. Fixed by adding the same
  `CopyToOutputDirectory` `None` item for `LICENSE` in `SmartPlugControl.csproj`. This one actually
  matters for GPL-3.0 compliance (section 4/5 require the license text accompany conveyed copies),
  unlike THIRD-PARTY-NOTICES.md which is just good practice for the MIT deps.
- A second reviewer (`daleghent`, a NINA contributor, not the maintainer) argued the AI-authored code
  was likely "cribbed" from the ecosystem's mostly-MIT plugins in general (an argument about LLM
  training-data provenance broadly, not this specific file) - responded by pointing out the actual
  official plugin scaffolding template (`isbeorn/nina.plugin.template`) that this project's structure
  visibly follows is **Unlicense** (public domain), not MIT, and that a diffuse "trained on mostly-MIT
  code in general" claim isn't the same kind of traceable, specific-source claim as the piekstra/
  python-kasa situation that actually drove the GPL-3.0 relicense. isbeorn (the actual maintainer) did
  not push back on GPL-3.0 itself - his review comment instead argued the opposite (that consulting
  GPL code to learn an undocumented protocol doesn't automatically make the result GPL, since request
  formats/field names/endpoints are protocol behavior, not copyrightable expression). Kept GPL-3.0
  anyway as the conservative choice already made - never had to resolve whose interpretation was
  "more correct" to get the PR merged.

**PR merged 2026-08-29 (isbeorn/nina.plugin.manifests#589).** Confirmed working: a fresh
install-from-repository inside real NINA succeeds and the plugin loads correctly. Fixing the v0.0.0.5
zip-packaging bug (see Changelog/gotchas below) was the last blocker - once that shipped, the PR was
merged without further back-and-forth. The plugin is now publicly listed and installable by anyone
through NINA's built-in plugin manager, not just via manual zip installation from GitHub Releases.

**v0.0.0.7 manifest PR merged 2026-09-04 (isbeorn/nina.plugin.manifests#677).** Followed the same
fork-resync workflow as #589 (the crepusculum-space fork's `main` had again diverged from
`upstream/main` since other plugins' PRs merged in the meantime - reset to `upstream/main` and
force-pushed before branching for this update; this is now the established, repeatable pattern for
every future manifest update, not a one-off). Also the first release to carry a real plugin logo
(`docs/logo.png`, `FeaturedImageURL`) instead of NINA's generic placeholder icon - confirmed via
NINA's own source (`AvailablePluginsView.xaml`/`PluginsView.xaml`) that a single manifest field drives
both the small icon in the plugin list and the larger image on the plugin's detail/Options page (just
rendered at different sizes) - no separate "list icon" vs "options icon" field exists. Real-world logo
size convention (checked against two other published plugins' actual files, not documented anywhere
in the manifest schema) is 1024x1024 PNG with a transparent background - the schema itself only
requires `FeaturedImageURL` to be a valid URI, no dimension constraint.

## Gotchas already paid for — don't re-learn these

- **A `DependencyProperty` registered with a default value that a real bound value can legitimately
  equal on the very first bind can silently never fire its PropertyChangedCallback at all - not just
  "less often", literally never, for exactly the users who need it most.** `PasswordBoxAssistant`'s
  `BoundPassword` attached property was registered with `PropertyMetadata(string.Empty, ...)`. For
  every first-time setup (no password saved yet), `TpLinkPassword`'s getter returns `""` - identical
  to the registered default - on the very first bind. WPF can skip invoking the callback entirely in
  that case, so `PasswordChanged` was never subscribed, and typing into the box did nothing, forever,
  with zero error anywhere (see the DPAPI gotcha below for why this was first misdiagnosed as a
  encryption failure instead). Fixed by registering the default as `null` instead - a real password is
  never null, so the very first bind is always seen as a genuine change. General lesson: an attached
  property's registered default should be a value no legitimate bound value can ever equal, not
  whatever the "empty" case happens to look like.
- **A background poll loop that retries a failing login on every cycle with no backoff can trip a
  third-party service's own rate limiting and lock a real user's account** - not hypothetical, this
  actually happened. Before this was fixed, `PlugRegistryService.RefreshAsync`'s equipment-page poll
  loop (`PlugControlDockableVM`, default every 10s) retried `cloudClient.LoginAsync` every single
  cycle whenever the cached token was null (which it stays, forever, if login keeps failing) - so a
  wrong-but-non-empty password (as opposed to a simply-unconfigured one, already handled) meant
  hammering TP-Link's cloud login endpoint every 10 seconds indefinitely. A user testing the
  PasswordBoxAssistant fix above hit this directly: repeated failed attempts eventually returned a
  second, undocumented error code (`-20661`, not one of TapoConnect's own named codes either - see
  `TapoException.ThrowFromErrorCode`) and their real TP-Link account came back "your account was
  locked" when checked directly at tplinkcloud.com. Fixed by adding `hasConnectedSuccessfully`
  tracking to `PlugRegistryService`: the poll loop's `RefreshAsync(isBackgroundPoll: true, ...)` now
  makes no network attempt at all - not even a login - until the *current* username/password have
  authenticated successfully at least once via an explicit "Refresh Plug List" click
  (`RefreshAsync(isBackgroundPoll: false, ...)`, or the parameterless overload, which manual call
  sites use). Changing credentials resets this, so a fresh manual success is required again before
  the poll loop will retry on its own. Also translated the specific known-empirically-wrong-password
  code (`-20601`, also not a TapoConnect-named code) to a plain-language message in
  `TpLinkCloudClient.LoginAsync` - a bare numeric error code means nothing to a non-technical user.
- **A release zip that installs fine from a manually-copied dev build can still be broken for a real
  install-from-NINA's-repository - these are not the same test.** Every release zip through v0.0.0.4
  wrapped the plugin's files in a top-level `Crepusculum.NINA.SmartPlugControl\` folder (zipping the
  build output *directory* rather than its *contents*). NINA's plugin manager extracts an installer
  archive straight into `<PluginsDir>\Smart Plug Control\` with no extra subfolder of its own - so
  every prior release actually produced `...\Smart Plug Control\Crepusculum.NINA.SmartPlugControl\*.dll`,
  one directory level too deep for NINA to find the DLL at all. This went unnoticed for four releases
  because the dev-loop `PostBuild` xcopy step (used for every manual test all along) already deploys
  at the correct depth - it was never exercised through the actual repository-install code path until
  someone finally tried it for real after the manifest PR was merged. Fixed in v0.0.0.5 by adding
  `scripts/package-release.ps1`, which zips the build output's contents, not the folder itself - and
  by building a local test harness (`scripts/serve-test-repository.ps1`, simulates NINA's plugin
  repository locally) specifically so this class of bug can be caught *before* a manifest PR is
  submitted, not after. **Use that harness before every future manifest PR/version bump - a clean
  `dotnet build` and a manually-copied dev deploy prove nothing about whether the packaged release zip
  actually installs correctly through NINA's real plugin manager.**

- **`hostCount` in `LocalPresenceResolver.GetLocalIPv4Subnets` includes the network and broadcast
  addresses, so comparing it directly against `MaxHostsPerSubnet` is an off-by-two bug that silently
  skips ordinary networks.** A /24 subnet computes `hostCount = 256` (`~mask + 1`), which is *greater
  than* the original cap of 254 usable hosts - so the entire subnet was skipped, no pings were ever
  sent, the ARP cache was never actually warmed by us, and every single cloud device failed the local
  presence check (empty plug list, no errors logged, since nothing threw - `WarmArpCacheAsync`
  completed "successfully" having done nothing). Caught immediately during first real-hardware testing
  of this pivot ("Refresh plug list ne fonctionne plus, il ne retourne rien"). Fixed by comparing
  usable-host count (`hostCount - 2`) against the cap instead of the raw value. If local presence
  detection ever silently finds nothing again on a normal network, check this arithmetic first.
- **A Tapo device on firmware ≥ ~1.4.0 rejects local KLAP control with `HttpResponseException:
  Forbidden` at `handshake1`, even with the correct TP-Link account credentials and a device that's
  genuinely on the same local network and same account** - not a bug in this plugin. TP-Link added a
  toggle (**"Third-Party Compatibility"**, in the Tapo app's device settings) that newer firmware
  requires to be explicitly enabled before local/third-party API access works at all; without it,
  the device refuses the handshake outright regardless of credentials. Confirmed against real
  hardware: 2x Kasa-branded KP125M (older `SMART.KASAPLUG` firmware) worked immediately with no such
  toggle needed, while the actual Tapo-branded P115 needed it explicitly enabled - same account, same
  network, same code path. Matches an identical report against the same `TapoConnect` library
  (github.com/cwakefie27/TapoConnect/issues/8: P110 on firmware 1.4.0 failed the same way, P125M on
  1.3.2 didn't) and a corresponding Home Assistant community fix. This is a **per-device Tapo-app
  setting**, not something this plugin can detect or work around in code - if a KLAP device fails
  login with `Forbidden` specifically (as opposed to a different KLAP exception), check this setting
  before assuming a code bug.
- **Re-authenticating on every poll cycle is what actually trips TP-Link's cloud rate limit, not
  moderate polling in general.** `PlugRegistryService.RefreshAsync` originally called
  `cloudClient.LoginAsync` fresh on every single refresh (a deliberate "keep it simple for now"
  choice from Phase 1b) - with the default 10s equipment-page refresh interval that's 360 logins/hour.
  TP-Link publishes no official rate limit numbers, but real-world reports from other TP-Link cloud
  integrations (e.g. Homey's NINA-unrelated integration hitting `-20004 API rate limit exceeded`)
  specifically blamed excessive re-authentication, not read-only polling of an already-valid session.
  Fixed: the cloud token is now cached (`cachedToken` field) and reused across refreshes, only
  re-acquired if a call using it fails, or if the TP-Link username/password changed since it was
  obtained (tracked via `cachedTokenUsername`/`cachedTokenPassword`, so editing credentials in Options
  doesn't keep using the previous account's session). If you add a second independent poll loop later
  (e.g. for Phase 6 consumption alerts), make sure it goes through this same cached-token path rather
  than calling `LoginAsync` on its own.
- **A stale deployed DLL will waste hours of your time looking for a phantom bug.** NINA's
  `PostBuild` xcopy step used to have the `/c` ("continue on error") flag, which silently swallowed
  "Sharing violation" errors when NINA (or a lingering background NINA process - closing the window
  doesn't always kill the process!) had the DLL locked. The build would report "Build succeeded" while
  actually deploying nothing, so NINA kept running an old version for hours while new sequencer
  Conditions/Triggers were debugged as if they had a MEF-loading bug that didn't actually exist. Fixed:
  `/c` removed, so a locked file now fails the build loudly instead of silently no-opping. **If you
  ever change plugin code and it doesn't seem to take effect in NINA, first compare
  `Get-FileHash` on the build output vs `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Crepusculum.NINA.SmartPlugControl\Crepusculum.NINA.SmartPlugControl.dll`
  before assuming there's a real bug.** If the build fails with `MSB3073`/"Sharing violation", check
  `Get-Process | Where-Object ProcessName -match "NINA"` for a lingering process and kill it.
- **Don't trust a reference library's code without cross-checking a second source.** The LED command
  shape from `piekstra/tplink-cloud-api` (`{"set_led_off":{"off":n}}`, not nested under `"system"`)
  turned out to be wrong — it silently did nothing (no error, no effect) until cross-checked against
  `python-kasa`, which showed it must be `{"system":{"set_led_off":{"off":n}}}`. Because of this,
  `KasaCloudPassthroughClient.ThrowIfDeviceError` now treats a missing/unexpected response shape as an
  error instead of silently succeeding — don't remove that safety net.
- **Class Library plugin projects don't copy NuGet dependencies to the output directory by default**
  the way Exe projects do, but NINA loads the plugin DLL directly from its own folder with no host
  process to supply missing dependencies. `SmartPlugControl.csproj` sets
  `CopyLocalLockFileAssemblies=true` plus `ExcludeAssets="runtime;native"` on the `NINA.Plugin` package
  reference specifically (see the "Resolved" note above for why `native` had to be added explicitly),
  so only genuinely plugin-specific dependencies get bundled — confirmed by a clean rebuild
  (2026-07-27) to be exactly `TapoConnect.dll` and `BouncyCastle.Crypto.dll`, nothing else.
  `System.Security.Cryptography.ProtectedData` has a `PackageReference` too (and is actually called
  from `SecureCredentialStore.cs`), but a clean build never copies its DLL either - it's apparently
  satisfied by the shared .NET Windows Desktop runtime NINA itself already requires, not something we
  redistribute. NINA's own remaining assemblies (and their huge dependency tree: Accord,
  EntityFramework, OxyPlot, ASCOM...) are provided by the host and must NOT be duplicated into the
  plugin folder (risk of assembly version conflicts, unnecessary bloat). If you add a new
  PackageReference and NINA fails to load the plugin with `FileNotFoundException`, check
  `%LOCALAPPDATA%\NINA\Logs\` first.
- **`GeometryGroup`/other `Freezable`s built in a ViewModel constructor must be `.Freeze()`d.** MEF
  composition can run a plugin VM's constructor on a non-UI thread; an unfrozen `Freezable` is
  thread-affine and throws "Must create DependencySource on same Thread as the DependencyObject" the
  first time WPF actually renders it on the real UI thread.
- **`Run.Text` binds TwoWay by default in WPF**, unlike `TextBlock.Text` (OneWay by default). Binding
  a read-only property to `Run.Text` throws `InvalidOperationException` at first render, not at
  compile/build time. Always pin `Mode=OneWay` explicitly on `Run.Text` bindings to computed/read-only
  properties.
- **`dotnet build` cannot catch WPF/XAML runtime failures, or prove a deploy actually landed.** Every
  crash/non-appearance bug in this project's history only surfaced when the user actually opened the
  relevant panel/menu inside a real NINA instance. Don't report a change as "done" or "working" until
  it's been confirmed running inside NINA, not just compiled - and if something that should show up
  doesn't, verify the deployed DLL hash matches the build output *before* debugging the C# logic.
- **When NINA's own open-source repo (github.com/isbeorn/nina) can answer "why doesn't X work",
  fetch the real source instead of guessing or relying only on installed-DLL reflection.** Reading
  `SequencerFactory.cs`/`PluginLoader.cs`/`SequenceContainerView.xaml` directly resolved several "is
  this a NINA limitation or my bug" questions (e.g. confirming Instructions/Conditions/Triggers are
  composed identically via MEF, and that "Loop Conditions" simply isn't grouped by category) far
  faster than reflecting on the installed assemblies.
- **Never ask the user to paste TP-Link (or any) credentials into chat.** When testing needs real
  login, use a standalone console harness the user runs themselves in their own terminal (prompts for
  credentials with masked input, right there — see the `dotnet run --project ...KasaTest` pattern used
  during development, built in the scratchpad dir, not committed to the repo).

## Repo/build layout

- `SmartPlugControl/SmartPlugControl.csproj` — the actual plugin project (net8.0-windows, WPF). Its
  `PostBuild` target xcopies the whole output directory to
  `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Crepusculum.NINA.SmartPlugControl\` automatically on every build
  **and now fails loudly if that copy can't complete** (e.g. NINA still running) - see gotchas above.
  NINA must be fully closed (check for a lingering process, not just the window) before rebuilding.
- `SmartPlugControlCloud/` — TP-Link cloud discovery (`TpLinkCloudClient`) and Kasa cloud device
  control (`KasaCloudPassthroughClient`). `SecureCredentialStore` DPAPI-encrypts the stored password.
- `SmartPlugControlDrivers/` — `IPlugDriver` abstraction + `KasaCloudPlugDriver`/
  `KasaCloudPlugDriverFactory` (the only implementation right now).
- `SmartPlugControlServices/PlugRegistryService.cs` — MEF-exported singleton tying cloud discovery +
  drivers + per-profile persisted data (equipment name, protected flag, NINA-visibility flag — JSON
  blob via `PluginOptionsAccessor`) into one polled `PlugViewModel` list. `Plugs` is visibility-filtered
  (what the equipment page/sequencer see); `AllPlugs` is unfiltered (Options page management grid
  only). Single source of truth for plug actions (on/off/LED, single or bulk) — bulk actions only ever
  touch `Plugs` (visible), and `TurnOffAllAsync` additionally skips protected plugs.
- `SmartPlugControlDockables/` — the Imaging-tab equipment page (`PlugControlDockableVM` +
  `PlugRowViewModel` + XAML templates). Protected-plug shutdown requires two separate `MyMessageBox`
  confirmations here (a human is assumed to be present); the sequencer's `TurnOffPlugInstruction`
  hard-blocks instead via `IValidatable` (no human assumed).
- `SmartPlugControlSequenceItems/` — Instructions/Conditions/Trigger. Each entity type
  (`SequenceItem`/`SequenceCondition`/`SequenceTrigger`) has its own duplicated
  `Plug*Base` picker class (C# can't inherit more than one of these), all exposing the same
  `AvailablePlugs`/`SelectedPlugId`/`SelectedPlug` shape backed by `IPlugRegistryService.Plugs`.
  `SmartPlugControlSequenceItemsTemplates.xaml` has one shared `DataTemplate` per abstract base
  (WPF resolves implicit templates up the inheritance chain), plus one more specific template
  for `TurnOnPlugInstruction`'s extra delay field. No "Mini" (compact imaging-tab) templates yet -
  relying on NINA's default fallback rendering.
- `PlugVisibilityRowViewModel.cs` (plugin root namespace) — backs the Options page's plug-visibility
  management grid; wraps a `PlugViewModel` + calls `IPlugRegistryService.SetVisibleInNina`.

## dotnet CLI on this machine

`dotnet` isn't on PATH in the sandboxed shell tools — use the full path:
`& "C:\Program Files\dotnet\dotnet.exe" build "SmartPlugControl/SmartPlugControl.csproj"`
