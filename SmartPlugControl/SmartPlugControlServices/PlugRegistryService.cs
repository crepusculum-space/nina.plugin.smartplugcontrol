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
    /// Combines TP-Link cloud discovery, cloud-relayed device control (legacy-protocol Kasa only - see
    /// KasaCloudPassthroughClient for why control never touches the local network), and per-profile
    /// persisted data (equipment name, protected flag) into one unified, polled view of every plug/socket.
    /// Tapo and newer-generation "SMART.*"-protocol Kasa devices are discovered and listed but will
    /// never have a driver - their protocol (KLAP/securePassthrough) has no cloud-relay path at all,
    /// only direct local-IP access, which conflicts with this plugin's cloud-only design. Not a gap to
    /// fill later; a deliberate, permanent exclusion (see CLAUDE.md).
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

        // Kept across refreshes so the UI/sequencer can act on a plug (on/off/LED) without triggering
        // a full re-discovery first. Rebuilt wholesale on every RefreshAsync.
        private Dictionary<string, IPlugDriver> driversByPlugId = new Dictionary<string, IPlugDriver>();

        // Drivers are rebuilt from scratch every RefreshAsync, so a driver's own internal "does this
        // support energy monitoring" cache resets every cycle too - this persists that finding across
        // refreshes instead, so an unsupported plug (most models) isn't re-queried every poll forever.
        private readonly HashSet<string> knownPowerUnsupportedPlugIds = new HashSet<string>();

        [ImportingConstructor]
        public PlugRegistryService(IProfileService profileService) : this(profileService, new TpLinkCloudClient(), new KasaCloudPassthroughClient()) {
        }

        internal PlugRegistryService(IProfileService profileService, ITpLinkCloudClient cloudClient, KasaCloudPassthroughClient kasaClient) {
            this.profileService = profileService;
            this.cloudClient = cloudClient;
            this.kasaClient = kasaClient;
            pluginSettings = new PluginOptionsAccessor(profileService, PluginGuid);
            profileService.ProfileChanged += ProfileService_ProfileChanged;
        }

        public IReadOnlyList<PlugViewModel> Plugs => plugs;
        public IReadOnlyList<PlugViewModel> AllPlugs => allPlugs;

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            pluginSettings = new PluginOptionsAccessor(profileService, PluginGuid);
        }

        public async Task RefreshAsync(CancellationToken token = default) {
            string username = Settings.Default.TpLinkUsername;
            string password = SecureCredentialStore.Unprotect(Settings.Default.TpLinkPasswordProtected);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) {
                return;
            }

            await refreshLock.WaitAsync(token);
            try {
                if (cachedToken == null || cachedTokenUsername != username || cachedTokenPassword != password) {
                    // Credentials changed since the cached token was obtained (e.g. edited in Options) -
                    // discard it rather than keep using a session tied to the previous account.
                    cachedToken = await cloudClient.LoginAsync(username, password, token);
                    cachedTokenUsername = username;
                    cachedTokenPassword = password;
                }

                IReadOnlyList<CloudPlugDeviceInfo> cloudDevices;
                try {
                    cloudDevices = await cloudClient.ListDevicesAsync(cachedToken, token);
                } catch (Exception) when (!token.IsCancellationRequested) {
                    // Cached token stopped working (expired/invalidated) - log in exactly once more
                    // rather than falling back to logging in on every future cycle.
                    cachedToken = await cloudClient.LoginAsync(username, password, token);
                    cloudDevices = await cloudClient.ListDevicesAsync(cachedToken, token);
                }
                string cloudToken = cachedToken;
                var persisted = LoadPersistedData();

                var newPlugs = new List<PlugViewModel>();
                var newDrivers = new Dictionary<string, IPlugDriver>();

                foreach (var device in cloudDevices) {
                    token.ThrowIfCancellationRequested();

                    // Only the legacy "IOT.*" protocol generation (older Kasa models like HS103/KP303)
                    // is implemented. Tapo devices AND newer "SMART.*"-protocol Kasa models (e.g. the
                    // KP125M, confirmed via real DeviceType logging) share the same KLAP-based protocol -
                    // which has no cloud-relay path at all (every implementation talks straight to the
                    // device's local IP), so it will never be implemented here; this isn't a temporary
                    // gap, see CLAUDE.md. Checking the DeviceType prefix directly instead of just
                    // Brand != Kasa catches this case too, rather than attempting (and always failing)
                    // legacy passthrough commands against a device that can't answer them.
                    bool usesLegacyProtocol = device.DeviceType != null && device.DeviceType.StartsWith("IOT.", StringComparison.OrdinalIgnoreCase);
                    if (!usesLegacyProtocol) {
                        // Still list the device (for visibility only - it will never be controllable),
                        // so persisted config like the equipment name isn't lost.
                        var unsupportedData = persisted.TryGetValue(device.DeviceId, out var ud) ? ud : new PlugPersistedData { PlugId = device.DeviceId };
                        newPlugs.Add(ToViewModel(device, device.DeviceId, device.Alias, unsupportedData, isOn: null, power: null, supportsLed: false));
                        continue;
                    }

                    IReadOnlyList<IPlugDriver> deviceDrivers;
                    try {
                        deviceDrivers = await KasaCloudPlugDriverFactory.CreateAsync(kasaClient, device, cloudToken, token);
                    } catch (Exception ex) {
                        // Device is on the account but the cloud relay call failed (offline, etc).
                        Logger.Error($"Failed to create driver(s) for Kasa device '{device.Alias}' ({device.DeviceId})", ex);
                        var data = persisted.TryGetValue(device.DeviceId, out var d) ? d : new PlugPersistedData { PlugId = device.DeviceId };
                        newPlugs.Add(ToViewModel(device, device.DeviceId, device.Alias, data, isOn: null, power: null, supportsLed: false));
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

                        newPlugs.Add(ToViewModel(device, driver.PlugId, driver.Alias, data, isOn, power, deviceSupportsLed, deviceIsLedOn));
                    }
                }

                driversByPlugId = newDrivers;
                allPlugs = newPlugs;
                plugs = newPlugs.Where(p => p.IsVisibleInNina).ToList();
            } finally {
                refreshLock.Release();
            }
        }

        private static PlugViewModel ToViewModel(CloudPlugDeviceInfo device, string plugId, string alias, PlugPersistedData data, bool? isOn, PlugPowerReading power, bool supportsLed, bool? isLedOn = null) {
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
                LastPower = power
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
