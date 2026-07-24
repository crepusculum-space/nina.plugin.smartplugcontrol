using Settings = Crepusculum.NINA.SmartPlugControl.Properties.Settings;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers;
using Newtonsoft.Json;
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
    /// Combines TP-Link cloud discovery, cloud-relayed device control (Kasa only for now - see
    /// KasaCloudPassthroughClient for why control never touches the local network), and per-profile
    /// persisted data (equipment name, protected flag) into one unified, polled view of every plug/socket.
    /// Tapo devices are discovered and listed but have no driver yet (deferred, see plan).
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

        // Kept across refreshes so the UI/sequencer can act on a plug (on/off/LED) without triggering
        // a full re-discovery first. Rebuilt wholesale on every RefreshAsync.
        private Dictionary<string, IPlugDriver> driversByPlugId = new Dictionary<string, IPlugDriver>();

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
                // No long-lived token cache yet - a fresh login each refresh keeps this simple until
                // polling frequency (added in a later phase) makes that wasteful.
                string cloudToken = await cloudClient.LoginAsync(username, password, token);
                var cloudDevices = await cloudClient.ListDevicesAsync(cloudToken, token);
                var persisted = LoadPersistedData();

                var newPlugs = new List<PlugViewModel>();
                var newDrivers = new Dictionary<string, IPlugDriver>();

                foreach (var device in cloudDevices) {
                    token.ThrowIfCancellationRequested();

                    if (device.Brand != PlugBrand.Kasa) {
                        // Tapo control isn't implemented yet (see plan) - still list the device so
                        // persisted config isn't lost once it's supported, but with unknown state.
                        var tapoData = persisted.TryGetValue(device.DeviceId, out var td) ? td : new PlugPersistedData { PlugId = device.DeviceId };
                        newPlugs.Add(ToViewModel(device, device.DeviceId, device.Alias, tapoData, isOn: null, power: null, supportsLed: false));
                        continue;
                    }

                    IReadOnlyList<IPlugDriver> deviceDrivers;
                    try {
                        deviceDrivers = await KasaCloudPlugDriverFactory.CreateAsync(kasaClient, device, cloudToken, token);
                    } catch (Exception) {
                        // Device is on the account but the cloud relay call failed (offline, etc).
                        var data = persisted.TryGetValue(device.DeviceId, out var d) ? d : new PlugPersistedData { PlugId = device.DeviceId };
                        newPlugs.Add(ToViewModel(device, device.DeviceId, device.Alias, data, isOn: null, power: null, supportsLed: false));
                        continue;
                    }

                    foreach (var driver in deviceDrivers) {
                        newDrivers[driver.PlugId] = driver;
                        var data = persisted.TryGetValue(driver.PlugId, out var d) ? d : new PlugPersistedData { PlugId = driver.PlugId };

                        bool? isOn = null;
                        bool supportsLed = false;
                        bool? isLedOn = null;
                        PlugPowerReading power = null;
                        try {
                            isOn = await driver.IsOnAsync(token);
                            supportsLed = await driver.SupportsLedAsync(token);
                            if (supportsLed) {
                                isLedOn = await driver.IsLedOnAsync(token);
                            }
                            power = await driver.GetPowerAsync(token);
                        } catch (Exception) {
                            // Device went offline between discovery and polling - leave state unknown.
                        }

                        newPlugs.Add(ToViewModel(device, driver.PlugId, driver.Alias, data, isOn, power, supportsLed, isLedOn));
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
