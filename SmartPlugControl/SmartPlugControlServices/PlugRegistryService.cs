using Settings = Crepusculum.NINA.SmartPlugControl.Properties.Settings;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    /// <summary>
    /// Combines TP-Link cloud discovery, per-protocol device control, and per-profile persisted data
    /// (equipment name, protected flag) into one unified, polled view of every plug/socket. Discovery is
    /// always cloud-based (the user's TP-Link account is the authoritative device list), but every
    /// discovered device is first checked for local-network presence (see LocalPresenceResolver) -
    /// devices not physically reachable from this PC (e.g. the same account's plugs at a different
    /// observatory, or at home) are excluded entirely, not just hidden. Devices confirmed local are then
    /// routed by protocol, not user choice: legacy "IOT.*" Kasa devices (no local authentication at all)
    /// are still controlled via the cloud relay (KasaCloudPassthroughClient); "SMART.*" devices (Tapo and
    /// newer-generation Kasa, KLAP/securePassthrough protocol) are controlled directly over the local
    /// network (KlapPlugDriver) - safe for the multi-tenant threat model because the KLAP handshake
    /// itself is gated on the real TP-Link account credentials stored on the device since pairing (see
    /// CLAUDE.md "Architecture history" for the full reasoning).
    /// </summary>
    [Export(typeof(IPlugRegistryService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class PlugRegistryService : IPlugRegistryService, IDisposable {
        // Matches the [assembly: Guid(...)] in Properties/AssemblyInfo.cs - PluginOptionsAccessor needs
        // this plugin's identity, but this service (unlike the manifest) has no IPluginManifest.Identifier
        // to read it from at runtime, so it is duplicated here.
        private static readonly Guid PluginGuid = Guid.Parse("22f9cea5-3456-4594-856e-73840c20c30a");
        private const string PersistedDataSettingsKey = "PlugPersistedDataJson";

        private readonly IProfileService profileService;
        private readonly ITpLinkCloudClient cloudClient;
        private readonly KasaCloudPassthroughClient kasaClient;
        private readonly LocalPresenceResolver localPresenceResolver;
        private readonly SemaphoreSlim refreshLock = new SemaphoreSlim(1, 1);

        private IPluginOptionsAccessor pluginSettings;
        private List<PlugViewModel> allPlugs = new List<PlugViewModel>();
        private List<PlugViewModel> plugs = new List<PlugViewModel>();

        // Reused across refreshes instead of logging in every cycle - TP-Link's cloud API has no
        // documented rate limit, but real-world reports (e.g. Homey's integration) show repeated
        // re-authentication specifically is what triggers "-20004 API rate limit exceeded", not
        // moderate polling of already-authenticated endpoints. Cleared and re-acquired only when a
        // call using it actually fails.
        private string cachedToken;
        private string cachedTokenUsername;
        private string cachedTokenPassword;

        // Self-contained cooldown for the background poll loop after a login failure - deliberately
        // NOT "wait for some other caller to prove these credentials work first" (an earlier version
        // of this fix did that with a hasConnectedSuccessfully flag, gated so only isBackgroundPoll:
        // false could ever set it true). That design silently deadlocked: if the equipment page's
        // poll loop and the Options page's manual button don't share the same PlugRegistryService
        // instance (unconfirmed either way, but the deadlock is real either way it turns out), the
        // poll loop's own flag could never become true by itself, since a background poll is never
        // allowed to attempt a login at all until the flag is already true - so it stayed permanently
        // gated even after a real, confirmed-successful manual login elsewhere. This backoff design
        // has no such dependency: any single instance manages its own cooldown from its own attempts,
        // so it can't deadlock regardless of how many instances exist. A manual refresh always
        // attempts immediately (isBackgroundPoll: false is never subject to this cooldown); a
        // background poll skips only for a limited time after ITS OWN most recent failed attempt.
        private DateTime? lastLoginFailureUtc;
        private static readonly TimeSpan BackgroundLoginRetryCooldown = TimeSpan.FromMinutes(5);

        // Tracks the credentials of the most recent login *attempt*, successful or not - deliberately
        // separate from cachedTokenUsername/cachedTokenPassword (which only update on SUCCESS). The
        // "did credentials change" check below needs to compare against what was last tried, not what
        // last worked - otherwise, for as long as a login keeps failing, cachedTokenUsername/Password
        // stay at their initial null forever, so the same still-wrong credentials look "changed" on
        // literally every call, resetting lastLoginFailureUtc every time and defeating the cooldown
        // entirely (confirmed on real hardware: a bad password produced a fresh error notification
        // every ~10s poll cycle instead of backing off - the exact login-retry-storm this cooldown was
        // built to prevent in the first place - see CLAUDE.md).
        private string lastAttemptedUsername;
        private string lastAttemptedPassword;

        // Kept across refreshes so the UI/sequencer can act on a plug (on/off/LED) without triggering
        // a full re-discovery first. Rebuilt wholesale on every RefreshAsync.
        private Dictionary<string, IPlugDriver> driversByPlugId = new Dictionary<string, IPlugDriver>();

        // Drivers are rebuilt from scratch every RefreshAsync, so a driver's own internal "does this
        // support energy monitoring" cache resets every cycle too - this persists that finding across
        // refreshes instead, so an unsupported plug (most models) isn't re-queried every poll forever.
        private readonly HashSet<string> knownPowerUnsupportedPlugIds = new HashSet<string>();

        [ImportingConstructor]
        public PlugRegistryService(IProfileService profileService) : this(profileService, new TpLinkCloudClient(), new KasaCloudPassthroughClient(), new LocalPresenceResolver()) {
        }

        internal PlugRegistryService(IProfileService profileService, ITpLinkCloudClient cloudClient, KasaCloudPassthroughClient kasaClient, LocalPresenceResolver localPresenceResolver) {
            this.profileService = profileService;
            this.cloudClient = cloudClient;
            this.kasaClient = kasaClient;
            this.localPresenceResolver = localPresenceResolver;
            pluginSettings = new PluginOptionsAccessor(profileService, PluginGuid);
            profileService.ProfileChanged += ProfileService_ProfileChanged;
        }

        public IReadOnlyList<PlugViewModel> Plugs => plugs;
        public IReadOnlyList<PlugViewModel> AllPlugs => allPlugs;

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            pluginSettings = new PluginOptionsAccessor(profileService, PluginGuid);
        }

        public Task RefreshAsync(CancellationToken token = default) => RefreshAsync(isBackgroundPoll: false, token);

        /// <param name="isBackgroundPoll">
        /// True for the equipment page's automatic poll loop; false for an explicit user action (the
        /// "Refresh Plug List" button). Only a background poll is ever subject to
        /// BackgroundLoginRetryCooldown after a login failure - a manual refresh always attempts
        /// immediately, regardless of how recently a background attempt failed.
        /// </param>
        public async Task RefreshAsync(bool isBackgroundPoll, CancellationToken token = default) {
            string username = Settings.Default.TpLinkUsername;
            string password = SecureCredentialStore.Unprotect(Settings.Default.TpLinkPasswordProtected);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) {
                // Debug, not Error - this is the expected, silent, every-cycle state for a plugin
                // that's simply never been configured yet. Logged at all so a support session can
                // still tell "never configured" apart from "configured but silently failed to save"
                // (e.g. the DPAPI failure TpLinkPassword's setter now guards against) by bumping log
                // verbosity and checking whether refreshes were even being attempted.
                Logger.Debug($"SmartPlugControl: refresh skipped - {(string.IsNullOrWhiteSpace(username) ? "username" : "password")} not configured.");
                return;
            }

            if (lastAttemptedUsername != username || lastAttemptedPassword != password) {
                // Credentials changed since the last attempt (first-ever configuration, or edited in
                // Options) - a cooldown started by the *old* credentials failing says nothing about
                // whether these new ones will too, so give the new ones an immediate first try.
                lastLoginFailureUtc = null;
                lastAttemptedUsername = username;
                lastAttemptedPassword = password;
            }

            if (isBackgroundPoll && lastLoginFailureUtc != null && DateTime.UtcNow - lastLoginFailureUtc.Value < BackgroundLoginRetryCooldown) {
                Logger.Debug($"SmartPlugControl: background poll skipped - a login attempt failed within the last {BackgroundLoginRetryCooldown}; use Refresh Plug List to retry immediately.");
                return;
            }

            await refreshLock.WaitAsync(token);
            try {
                if (cachedToken == null || cachedTokenUsername != username || cachedTokenPassword != password) {
                    // Credentials changed since the cached token was obtained (e.g. edited in Options) -
                    // discard it rather than keep using a session tied to the previous account.
                    try {
                        cachedToken = await cloudClient.LoginAsync(username, password, token);
                    } catch (Exception) when (!token.IsCancellationRequested) {
                        lastLoginFailureUtc = DateTime.UtcNow;
                        throw;
                    }
                    cachedTokenUsername = username;
                    cachedTokenPassword = password;
                }

                IReadOnlyList<CloudPlugDeviceInfo> cloudDevices;
                try {
                    cloudDevices = await cloudClient.ListDevicesAsync(cachedToken, token);
                } catch (Exception) when (!token.IsCancellationRequested) {
                    // Cached token stopped working (expired/invalidated) - log in exactly once more
                    // rather than falling back to logging in on every future cycle.
                    try {
                        cachedToken = await cloudClient.LoginAsync(username, password, token);
                    } catch (Exception) when (!token.IsCancellationRequested) {
                        lastLoginFailureUtc = DateTime.UtcNow;
                        throw;
                    }
                    cloudDevices = await cloudClient.ListDevicesAsync(cachedToken, token);
                }
                lastLoginFailureUtc = null;
                string cloudToken = cachedToken;
                var persisted = LoadPersistedData();

                // Ping-sweep every locally-attached subnet once per refresh so the OS ARP cache is fresh
                // before resolving any device's MAC below (see LocalPresenceResolver).
                await localPresenceResolver.WarmArpCacheAsync(token);

                var newPlugs = new List<PlugViewModel>();
                var newDrivers = new Dictionary<string, IPlugDriver>();

                foreach (var device in cloudDevices) {
                    token.ThrowIfCancellationRequested();
                    string deviceIp = null;
                    bool? resolved = string.IsNullOrEmpty(device.DeviceMac) ? (bool?)null : TapoConnect.Util.TapoUtils.TryGetIpAddressByMacAddress(device.DeviceMac, out deviceIp);

                    // Legacy "IOT.*" devices (older Kasa models like HS103/KP303) have no local
                    // authentication at all, so they're controlled exclusively via the cloud relay -
                    // only TP-Link's own account-boundary check (the cloud login) can guarantee only
                    // this account's instance controls them. "SMART.*" devices (Tapo and newer-generation
                    // Kasa, e.g. the KP125M) use the KLAP/securePassthrough protocol, which has no
                    // cloud-relay path at all - but its handshake is itself gated on the real account
                    // credentials stored on the device since pairing, so local control is exactly as safe
                    // as cloud for these.
                    bool usesLegacyProtocol = device.DeviceType != null && device.DeviceType.StartsWith("IOT.", StringComparison.OrdinalIgnoreCase);

                    // Local presence only matters for KLAP devices, which control directly over the local
                    // network and simply cannot function without a locally-resolved IP - excluded
                    // entirely here, same as before (rather than shown as "known but uncontrollable").
                    // Legacy Kasa devices control exclusively through the cloud relay, which never needed
                    // local reachability at all - TP-Link's own login already guarantees the account
                    // boundary regardless of network topology. Requiring local presence for them too was
                    // really an unrelated convenience filter (avoid showing a device from a different one
                    // of the user's own sites, e.g. a home plug on the same account as an observatory
                    // instance) - and it silently broke real multi-tenant observatories that isolate each
                    // client's Ethernet VLAN from the shared WiFi VLAN their smart plugs sit on: the plug
                    // is genuinely at the site, just unreachable via ARP from this specific VLAN.
                    // Confirmed by a real user report - a legacy-protocol-adjacent device in that exact
                    // topology showed up as nothing at all. "Visible in NINA" remains the manual way to
                    // hide a truly unrelated device, rather than an automatic-but-unreliable network
                    // heuristic that only ever worked by accident on a single flat network.
                    if (!usesLegacyProtocol && resolved != true) {
                        continue;
                    }

                    if (!usesLegacyProtocol) {
                        // Reuses cached driver instances (and their KLAP login sessions) across refresh
                        // cycles instead of building fresh ones every time - see CLAUDE.md on why that
                        // matters (hammering a device's own local authentication every poll cycle caused
                        // real failures). Also probes whether this device has child outlets (a power
                        // strip, e.g. the P300 family/P316M) and returns one driver per outlet if so -
                        // TapoConnect itself has no concept of child outlets, see
                        // PowerStripKlapDeviceClient/KlapPlugDriverFactory.
                        IReadOnlyList<KlapPlugDriver> klapDrivers;
                        try {
                            klapDrivers = await KlapPlugDriverFactory.CreateAsync(device.DeviceId, device.Alias, deviceIp, username, password, driversByPlugId, token);
                        } catch (Exception ex) {
                            // Most likely: this device was actually paired under a different TP-Link
                            // account than the one configured in Options (shouldn't normally happen,
                            // since cloud discovery only returns this account's own devices - but this is
                            // the safety net that would catch it, and it's also the graceful failure path
                            // for a simply-offline device).
                            Logger.Error($"Failed to log in to or read state from KLAP device '{device.Alias}' ({device.DeviceId}) at {deviceIp}", ex);
                            continue;
                        }

                        foreach (var klapDriver in klapDrivers) {
                            var data = persisted.TryGetValue(klapDriver.PlugId, out var d) ? d : new PlugPersistedData { PlugId = klapDriver.PlugId };

                            bool? isOn = null;
                            PlugPowerReading power = null;
                            try {
                                isOn = await klapDriver.IsOnAsync(token);
                                if (!knownPowerUnsupportedPlugIds.Contains(klapDriver.PlugId)) {
                                    power = await klapDriver.GetPowerAsync(token);
                                    if (power == null) {
                                        knownPowerUnsupportedPlugIds.Add(klapDriver.PlugId);
                                    }
                                }
                            } catch (Exception ex) {
                                // One outlet on a power strip going offline/erroring shouldn't take the
                                // rest of the strip's outlets down with it.
                                Logger.Error($"Failed to read state from KLAP device '{klapDriver.Alias}' ({klapDriver.PlugId}) at {deviceIp}", ex);
                                continue;
                            }

                            newDrivers[klapDriver.PlugId] = klapDriver;
                            newPlugs.Add(ToViewModel(device, klapDriver.PlugId, klapDriver.Alias, data, isOn, power, supportsLed: false, supportsPowerMonitoring: !knownPowerUnsupportedPlugIds.Contains(klapDriver.PlugId)));
                        }
                        continue;
                    }

                    IReadOnlyList<IPlugDriver> deviceDrivers;
                    try {
                        deviceDrivers = await KasaCloudPlugDriverFactory.CreateAsync(kasaClient, device, cloudToken, token);
                    } catch (Exception ex) {
                        // Device is on the account but the cloud relay call failed (offline, etc).
                        Logger.Error($"Failed to create driver(s) for Kasa device '{device.Alias}' ({device.DeviceId})", ex);
                        var data = persisted.TryGetValue(device.DeviceId, out var d) ? d : new PlugPersistedData { PlugId = device.DeviceId };
                        newPlugs.Add(ToViewModel(device, device.DeviceId, device.Alias, data, isOn: null, power: null, supportsLed: false, supportsPowerMonitoring: !knownPowerUnsupportedPlugIds.Contains(device.DeviceId)));
                        continue;
                    }

                    // LED state is a whole-device setting, not per-socket (confirmed on real KP303
                    // hardware - see KasaCloudPlugDriver) - query it once per physical device instead
                    // of once per child driver, to avoid N identical redundant calls on a power strip.
                    bool deviceSupportsLed = false;
                    bool? deviceIsLedOn = null;
                    if (deviceDrivers.Count > 0) {
                        try {
                            deviceSupportsLed = await deviceDrivers[0].SupportsLedAsync(token);
                            if (deviceSupportsLed) {
                                deviceIsLedOn = await deviceDrivers[0].IsLedOnAsync(token);
                            }
                        } catch (Exception ex) {
                            // Leave LED state unknown for this device this cycle.
                            Logger.Error($"Failed to read LED state for '{device.Alias}' ({device.DeviceId})", ex);
                        }
                    }

                    foreach (var driver in deviceDrivers) {
                        newDrivers[driver.PlugId] = driver;
                        var data = persisted.TryGetValue(driver.PlugId, out var d) ? d : new PlugPersistedData { PlugId = driver.PlugId };

                        bool? isOn = null;
                        PlugPowerReading power = null;
                        try {
                            isOn = await driver.IsOnAsync(token);
                            if (!knownPowerUnsupportedPlugIds.Contains(driver.PlugId)) {
                                power = await driver.GetPowerAsync(token);
                                if (power == null) {
                                    knownPowerUnsupportedPlugIds.Add(driver.PlugId);
                                }
                            }
                        } catch (Exception ex) {
                            // Device went offline between discovery and polling - leave state unknown.
                            Logger.Error($"Failed to read on/off or power state for '{driver.Alias}' ({driver.PlugId})", ex);
                        }

                        newPlugs.Add(ToViewModel(device, driver.PlugId, driver.Alias, data, isOn, power, deviceSupportsLed, deviceIsLedOn, supportsPowerMonitoring: !knownPowerUnsupportedPlugIds.Contains(driver.PlugId)));
                    }
                }

                driversByPlugId = newDrivers;
                allPlugs = newPlugs;
                plugs = newPlugs.Where(p => p.IsVisibleInNina).ToList();
            } finally {
                refreshLock.Release();
            }
        }

        private static PlugViewModel ToViewModel(CloudPlugDeviceInfo device, string plugId, string alias, PlugPersistedData data, bool? isOn, PlugPowerReading power, bool supportsLed, bool? isLedOn = null, bool supportsPowerMonitoring = true) {
            return new PlugViewModel {
                PlugId = plugId,
                Alias = alias,
                Brand = device.Brand,
                EquipmentName = data.EquipmentName,
                IsProtected = data.IsProtected,
                IsVisibleInNina = data.IsVisibleInNina,
                IsOn = isOn,
                SupportsLed = supportsLed,
                IsLedOn = isLedOn,
                LastPower = power,
                SupportsPowerMonitoring = supportsPowerMonitoring,
                MaxAmpsAt12V = data.MaxAmpsAt12V,
                PsuEfficiencyPercent = data.PsuEfficiencyPercent
            };
        }

        public void SetEquipmentName(string plugId, string equipmentName) {
            var data = LoadPersistedData();
            if (!data.TryGetValue(plugId, out var entry)) {
                entry = new PlugPersistedData { PlugId = plugId };
                data[plugId] = entry;
            }
            entry.EquipmentName = equipmentName ?? string.Empty;
            SavePersistedData(data);

            // allPlugs and plugs share the same PlugViewModel instances (plugs is a filtered view over
            // allPlugs), so mutating the one found here is visible through either list.
            var plug = allPlugs.FirstOrDefault(p => p.PlugId == plugId);
            if (plug != null) {
                plug.EquipmentName = entry.EquipmentName;
            }
        }

        public void SetProtected(string plugId, bool isProtected) {
            var data = LoadPersistedData();
            if (!data.TryGetValue(plugId, out var entry)) {
                entry = new PlugPersistedData { PlugId = plugId };
                data[plugId] = entry;
            }
            entry.IsProtected = isProtected;
            SavePersistedData(data);

            var plug = allPlugs.FirstOrDefault(p => p.PlugId == plugId);
            if (plug != null) {
                plug.IsProtected = isProtected;
            }
        }

        public void SetMaxAmpsAt12V(string plugId, double amps) {
            var data = LoadPersistedData();
            if (!data.TryGetValue(plugId, out var entry)) {
                entry = new PlugPersistedData { PlugId = plugId };
                data[plugId] = entry;
            }
            entry.MaxAmpsAt12V = amps < 0 ? 0 : amps;
            SavePersistedData(data);

            var plug = allPlugs.FirstOrDefault(p => p.PlugId == plugId);
            if (plug != null) {
                plug.MaxAmpsAt12V = entry.MaxAmpsAt12V;
            }
        }

        public void SetPsuEfficiencyPercent(string plugId, int percent) {
            var data = LoadPersistedData();
            if (!data.TryGetValue(plugId, out var entry)) {
                entry = new PlugPersistedData { PlugId = plugId };
                data[plugId] = entry;
            }
            // 0% would divide by zero when converting to a Watts threshold.
            entry.PsuEfficiencyPercent = percent < 1 ? 1 : percent;
            SavePersistedData(data);

            var plug = allPlugs.FirstOrDefault(p => p.PlugId == plugId);
            if (plug != null) {
                plug.PsuEfficiencyPercent = entry.PsuEfficiencyPercent;
            }
        }

        public void SetVisibleInNina(string plugId, bool visible) {
            var data = LoadPersistedData();
            if (!data.TryGetValue(plugId, out var entry)) {
                entry = new PlugPersistedData { PlugId = plugId };
                data[plugId] = entry;
            }
            entry.IsVisibleInNina = visible;
            SavePersistedData(data);

            var plug = allPlugs.FirstOrDefault(p => p.PlugId == plugId);
            if (plug != null) {
                plug.IsVisibleInNina = visible;
            }
            // Membership in the filtered list changes, not just a property - recompute it.
            plugs = allPlugs.Where(p => p.IsVisibleInNina).ToList();
        }

        public Task TurnOnAsync(string plugId, CancellationToken token = default) =>
            GetDriverOrThrow(plugId).TurnOnAsync(token);

        public Task TurnOffAsync(string plugId, CancellationToken token = default) =>
            GetDriverOrThrow(plugId).TurnOffAsync(token);

        public Task SetLedAsync(string plugId, bool on, CancellationToken token = default) =>
            GetDriverOrThrow(plugId).SetLedAsync(on, token);

        // These bulk actions deliberately iterate `plugs` (visible only), not every discovered device
        // on the TP-Link account (`allPlugs`/`driversByPlugId`) - the account may include plugs
        // unrelated to the observatory (e.g. a home TV/printer) that the user has hidden precisely so
        // "Turn On/Off All" never touches them.
        public async Task TurnOnAllAsync(CancellationToken token = default) {
            foreach (var plugId in plugs.Select(p => p.PlugId)) {
                if (!driversByPlugId.TryGetValue(plugId, out var driver)) {
                    continue;
                }
                token.ThrowIfCancellationRequested();
                await driver.TurnOnAsync(token);
            }
        }

        public async Task TurnOffAllAsync(CancellationToken token = default) {
            foreach (var plug in plugs) {
                if (plug.IsProtected || !driversByPlugId.TryGetValue(plug.PlugId, out var driver)) {
                    continue;
                }
                token.ThrowIfCancellationRequested();
                await driver.TurnOffAsync(token);
            }
        }

        public async Task SetAllLedsAsync(bool on, CancellationToken token = default) {
            foreach (var plugId in plugs.Select(p => p.PlugId)) {
                if (!driversByPlugId.TryGetValue(plugId, out var driver)) {
                    continue;
                }
                token.ThrowIfCancellationRequested();
                if (await driver.SupportsLedAsync(token)) {
                    await driver.SetLedAsync(on, token);
                }
            }
        }

        private IPlugDriver GetDriverOrThrow(string plugId) {
            if (!driversByPlugId.TryGetValue(plugId, out var driver)) {
                throw new InvalidOperationException($"No live driver for plug '{plugId}' - it may be offline or not yet discovered. Refresh first.");
            }
            return driver;
        }

        private Dictionary<string, PlugPersistedData> LoadPersistedData() {
            string json = pluginSettings.GetValueString(PersistedDataSettingsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) {
                return new Dictionary<string, PlugPersistedData>();
            }
            try {
                var list = JsonConvert.DeserializeObject<List<PlugPersistedData>>(json) ?? new List<PlugPersistedData>();
                return list.ToDictionary(p => p.PlugId);
            } catch (JsonException) {
                return new Dictionary<string, PlugPersistedData>();
            }
        }

        private void SavePersistedData(Dictionary<string, PlugPersistedData> data) {
            pluginSettings.SetValueString(PersistedDataSettingsKey, JsonConvert.SerializeObject(data.Values.ToList()));
        }

        public void Dispose() {
            profileService.ProfileChanged -= ProfileService_ProfileChanged;
        }
    }
}
