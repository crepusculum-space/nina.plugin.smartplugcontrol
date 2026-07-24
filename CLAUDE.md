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

- **Kasa: done and working**, verified against real hardware (HS103 x2, KP303 power strip).
  Discovery, on/off, LED, and per-socket state all work through the cloud relay.
- **Tapo: not implemented, deliberately deferred.** The user doesn't have Tapo hardware to test
  against. Tapo's local protocol (KLAP/securePassthrough) needs a real device to confirm whether the
  same cloud-relay trick even works for it — don't assume it does. Tapo devices are still *discovered*
  (`PlugRegistryService` lists them) but have no driver, so they show up with unknown state.
- **Equipment page (dockable panel)**: done, renders inside NINA's **Imaging** tab (not the
  Equipment tab — that surprised the user at first, who expected it under Installed Plugins settings
  instead). **Placement is a deliberate, final decision, not a placeholder**: the user considered
  moving it and decided to keep it in Imaging, since the toolbar icon there lets them show/hide it
  without leaving the Imaging view mid-session. Don't relocate this without being asked again.
- **Not started yet**: sequencer instructions/conditions/triggers, alerts, the real settings page
  (currently just two plaintext credential fields bolted onto Options.xaml as a stopgap), power-usage
  threshold UI, Tapo support.

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
  BouncyCastle.Crypto) get bundled — NINA's own assemblies (and their huge dependency tree: Accord,
  EntityFramework, OxyPlot, ASCOM...) are provided by the host and must NOT be duplicated into the
  plugin folder (risk of assembly version conflicts, and it's unnecessary bloat). If you add a new
  PackageReference and NINA fails to load the plugin with `FileNotFoundException`, check
  `%LOCALAPPDATA%\NINA\Logs\` first — check whether the new dependency's DLL made it into
  `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Crepusculum.NINA.SmartPlugControl\`.
- **`GeometryGroup`/other `Freezable`s built in a ViewModel constructor must be `.Freeze()`d.** MEF
  composition can run a plugin VM's constructor on a non-UI thread; an unfrozen `Freezable` is
  thread-affine and throws "Must create DependencySource on same Thread as the DependencyObject" the
  first time WPF actually renders it on the real UI thread.
- **`Run.Text` binds TwoWay by default in WPF**, unlike `TextBlock.Text` (OneWay by default). Binding
  a read-only property to `Run.Text` throws `InvalidOperationException` at first render, not at
  compile/build time. Always pin `Mode=OneWay` explicitly on `Run.Text` bindings to computed/read-only
  properties.
- **`dotnet build` cannot catch WPF/XAML runtime failures.** Every crash above only surfaced when the
  user actually opened the panel inside a real NINA instance. Don't report a UI change as "done" or
  "working" until it's been confirmed running inside NINA, not just compiled.
- **Never ask the user to paste TP-Link (or any) credentials into chat.** When testing needs real
  login, use a standalone console harness the user runs themselves in their own terminal (prompts for
  credentials with masked input, right there — see the `dotnet run --project ...KasaTest` pattern used
  during development, built in the scratchpad dir, not committed to the repo).

## Repo/build layout

- `SmartPlugControl/SmartPlugControl.csproj` — the actual plugin project (net8.0-windows, WPF). Its
  `PostBuild` target xcopies the whole output directory to
  `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Crepusculum.NINA.SmartPlugControl\` automatically on every build,
  so a `dotnet build` is enough to deploy a locally-testable copy — no separate install step.
- `SmartPlugControlCloud/` — TP-Link cloud discovery (`TpLinkCloudClient`) and Kasa cloud device
  control (`KasaCloudPassthroughClient`). `SecureCredentialStore` DPAPI-encrypts the stored password.
- `SmartPlugControlDrivers/` — `IPlugDriver` abstraction + `KasaCloudPlugDriver`/
  `KasaCloudPlugDriverFactory` (the only implementation right now).
- `SmartPlugControlServices/PlugRegistryService.cs` — MEF-exported singleton tying cloud discovery +
  drivers + per-profile persisted data (equipment name, protected flag — JSON blob via
  `PluginOptionsAccessor`) into one polled `PlugViewModel` list. Also the single source of truth for
  plug actions (on/off/LED, single or bulk) — `TurnOffAllAsync` silently skips protected plugs.
- `SmartPlugControlDockables/` — the Imaging-tab equipment page (`PlugControlDockableVM` +
  `PlugRowViewModel` + XAML templates). Protected-plug shutdown requires two separate `MyMessageBox`
  confirmations here; a future sequencer instruction should hard-block instead (not yet built).

## dotnet CLI on this machine

`dotnet` isn't on PATH in the sandboxed shell tools — use the full path:
`& "C:\Program Files\dotnet\dotnet.exe" build "SmartPlugControl/SmartPlugControl.csproj"`
