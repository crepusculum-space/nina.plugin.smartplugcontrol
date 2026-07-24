# Smart Plug Control — NINA plugin

Controls TP-Link Kasa/Tapo smart plugs from NINA (Nighttime Imaging 'N' Astronomy) sequences.

## Who this is for and why it's built this way

This plugin is meant to run in **multi-tenant commercial remote observatories**, not just
backyard/personal setups. Some observatories isolate each client's equipment with VLANs, but that
isn't guaranteed everywhere. This drives the single most important architectural constraint in the
whole project:

**Plug control goes exclusively through the TP-Link cloud API — never a direct local-network
connection to the device.** A local-network driver would let one client's NINA instance reach and
control *any* device on a flat/shared network, regardless of which TP-Link account owns it. Routing
control through the cloud means TP-Link enforces the account boundary server-side: a login can only
ever control devices linked to that same account, independent of network topology. This was a
deliberate pivot mid-project (see git history: "Replace local-network Kasa control with cloud-relay-
only control") — do not reintroduce local IP-based control (e.g. via `Aldaviva.Kasa`, or any direct
`http://<device-ip>` calls) without re-litigating this constraint with the user first.

## Current status

**Done and verified working inside a real running NINA instance** (not just compiled):
- Cloud discovery, on/off, LED control for Kasa (HS103 x2, KP303 power strip - real hardware).
- Per-plug "visible in NINA" toggle (Options page) - the TP-Link account may have devices unrelated
  to the observatory (a home TV, a printer); hidden plugs are excluded everywhere, including bulk
  actions (`TurnOnAllAsync`/`TurnOffAllAsync`/`SetAllLedsAsync`).
- Equipment page (dockable panel) in NINA's **Imaging** tab. This placement is a **deliberate, final
  decision** (the user considered the Equipment tab / Installed Plugins settings, chose Imaging
  because the toolbar icon there lets it be shown/hidden without leaving the Imaging view) - don't
  relocate without being asked again.
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
- **Tapo support**: no hardware to test against. Tapo's local protocol (KLAP/securePassthrough) needs
  a real device to confirm whether the cloud-relay trick even works for it - don't assume it does.
  Tapo devices are still *discovered* (`PlugRegistryService` lists them) but have no driver.

**Not started**: real settings page (currently just 2 plaintext credential fields + a plug-visibility
grid bolted onto Options.xaml as a stopgap - no threshold/refresh-interval/LED-start-end-of-sequence
settings yet), consumption-threshold alerts/notifications, community-facing docs for adding other
brands.

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

## Gotchas already paid for — don't re-learn these

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
  `CopyLocalLockFileAssemblies=true` plus `ExcludeAssets="runtime"` on the `NINA.Plugin` package
  reference specifically, so only genuinely plugin-specific dependencies (TapoConnect,
  BouncyCastle.Crypto, System.Security.Cryptography.ProtectedData) get bundled — NINA's own assemblies
  (and their huge dependency tree: Accord, EntityFramework, OxyPlot, ASCOM...) are provided by the host
  and must NOT be duplicated into the plugin folder (risk of assembly version conflicts, unnecessary
  bloat). If you add a new PackageReference and NINA fails to load the plugin with
  `FileNotFoundException`, check `%LOCALAPPDATA%\NINA\Logs\` first.
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
