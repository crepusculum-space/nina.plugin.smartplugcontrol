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
using TapoConnect.Util;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices {
    /// <summary>
    /// Combines TP-Link cloud discovery, local brand-specific drivers, and per-profile persisted data
    /// (equipment name, protected flag) into one unified, polled view of every plug/socket.
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
        private readonly SemaphoreSlim refreshLock = new SemaphoreSlim(1, 1);

        private IPluginOptionsAccessor pluginSettings;
        private Dictionary<string, IPlugDriver> driversByPlugId = new Dictionary<string, IPlugDriver>();
        private List<PlugViewModel> plugs = new List<PlugViewModel>();

        [ImportingConstructor]
        public PlugRegistryService(IProfileService profileService) : this(profileService, new TpLinkCloudClient()) {
        }

        internal PlugRegistryService(IProfileService profileService, ITpLinkCloudClient cloudClient) {
            this.profileService = profileService;
            this.cloudClient = cloudClient;
            pluginSettings = new PluginOptionsAccessor(profileService, PluginGuid);
            profileService.ProfileChanged += ProfileService_ProfileChanged;
        }

        public IReadOnlyList<PlugViewModel> Plugs => plugs;

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
                var cloudDevices = await cloudClient.DiscoverDevicesAsync(username, password, token);
                var persisted = LoadPersistedData();

                var newDrivers = new Dictionary<string, IPlugDriver>();
                var newPlugs = new List<PlugViewModel>();

                foreach (var device in cloudDevices) {
                    token.ThrowIfCancellationRequested();

                    // The cloud device list carries no local IP; opportunistically resolve it from the
                    // MAC via ARP. If the device hasn't recently talked on the LAN this will fail, in
                    // which case the plug still shows up (from the cloud) but with unknown live state.
                    string ip = await Task.Run(() => TapoUtils.GetIpAddressByMacAddress(device.DeviceMac), token);

                    IReadOnlyList<IPlugDriver> deviceDrivers = Array.Empty<IPlugDriver>();
                    if (ip != null) {
                        deviceDrivers = await CreateDriversAsync(device, ip, username, password);
                    }

                    if (deviceDrivers.Count == 0) {
                        // Either the IP couldn't be resolved, or the device connection failed - still
                        // surface it in the list (persisted config isn't lost) but with unknown state.
                        var data = persisted.TryGetValue(device.DeviceId, out var d) ? d : new PlugPersistedData { PlugId = device.DeviceId };
                        newPlugs.Add(ToViewModel(device, device.DeviceId, data, ip, isOn: null, power: null));
                        continue;
                    }

                    foreach (var driver in deviceDrivers) {
                        newDrivers[driver.PlugId] = driver;
                        var data = persisted.TryGetValue(driver.PlugId, out var d) ? d : new PlugPersistedData { PlugId = driver.PlugId };

                        bool? isOn = null;
                        PlugPowerReading power = null;
                        try {
                            isOn = await driver.IsOnAsync(token);
                            power = await driver.GetPowerAsync(token);
                        } catch (Exception) {
                            // Device went offline between discovery and polling - leave state unknown.
                        }

                        newPlugs.Add(ToViewModel(device, driver.PlugId, data, ip, isOn, power));
                    }
                }

                await DisposeDriversAsync(driversByPlugId.Values.Except(newDrivers.Values));

                driversByPlugId = newDrivers;
                plugs = newPlugs;
            } finally {
                refreshLock.Release();
            }
        }

        private static PlugViewModel ToViewModel(CloudPlugDeviceInfo device, string plugId, PlugPersistedData data, string ip, bool? isOn, PlugPowerReading power) {
            return new PlugViewModel {
                PlugId = plugId,
                Alias = device.Alias,
                Brand = device.Brand,
                EquipmentName = data.EquipmentName,
                IsProtected = data.IsProtected,
                LocalIpAddress = ip,
                IsOn = isOn,
                LastPower = power
            };
        }

        private static async Task<IReadOnlyList<IPlugDriver>> CreateDriversAsync(CloudPlugDeviceInfo device, string ip, string username, string password) {
            try {
                if (device.Brand == PlugBrand.Kasa) {
                    return await KasaPlugDriverFactory.CreateAsync(device.DeviceId, ip);
                }
                if (device.Brand == PlugBrand.Tapo) {
                    return new List<IPlugDriver> { new TapoPlugDriver(device.DeviceId, ip, username, password) };
                }
            } catch (Exception) {
                // Device is on the cloud account but unreachable/incompatible locally right now.
            }
            return Array.Empty<IPlugDriver>();
        }

        private static async Task DisposeDriversAsync(IEnumerable<IPlugDriver> drivers) {
            foreach (var driver in drivers) {
                try {
                    await driver.DisposeAsync();
                } catch (Exception) {
                    // Best-effort cleanup.
                }
            }
        }

        public void SetEquipmentName(string plugId, string equipmentName) {
            var data = LoadPersistedData();
            if (!data.TryGetValue(plugId, out var entry)) {
                entry = new PlugPersistedData { PlugId = plugId };
                data[plugId] = entry;
            }
            entry.EquipmentName = equipmentName ?? string.Empty;
            SavePersistedData(data);

            var plug = plugs.FirstOrDefault(p => p.PlugId == plugId);
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

            var plug = plugs.FirstOrDefault(p => p.PlugId == plugId);
            if (plug != null) {
                plug.IsProtected = isProtected;
            }
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
            foreach (var driver in driversByPlugId.Values) {
                driver.DisposeAsync().AsTask().Wait();
            }
        }
    }
}
