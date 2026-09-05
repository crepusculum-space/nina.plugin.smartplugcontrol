using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TapoConnect.Dto;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// A KLAP-family device (Tapo/newer-generation Kasa) can be a single outlet or a multi-outlet power
    /// strip (e.g. the P300 family/P316M); this is only knowable by asking the device itself
    /// (get_child_device_list). This factory does that probe once and returns one
    /// <see cref="KlapPlugDriver"/> per physical outlet - mirroring how
    /// <see cref="KasaCloudPlugDriverFactory"/> does the same thing for legacy Kasa power strips.
    /// </summary>
    public static class KlapPlugDriverFactory {
        public static async Task<IReadOnlyList<KlapPlugDriver>> CreateAsync(
            string deviceId,
            string alias,
            string deviceIp,
            string username,
            string password,
            IReadOnlyDictionary<string, IPlugDriver> existingDrivers,
            CancellationToken token = default) {

            KlapPlugDriver ReuseOrCreate(string plugId, string driverAlias, string childDeviceId) =>
                existingDrivers.TryGetValue(plugId, out var existing) &&
                existing is KlapPlugDriver existingKlapDriver && existingKlapDriver.DeviceIp == deviceIp
                    ? existingKlapDriver
                    : new KlapPlugDriver(plugId, driverAlias, deviceIp, username, password, childDeviceId);

            // Represents the whole physical device for now - only used to probe for children and,
            // if there turn out to be none, returned as-is (an ordinary single-outlet plug).
            var parentDriver = ReuseOrCreate(deviceId, alias, null);

            List<DeviceGetInfoResult> children;
            try {
                children = await parentDriver.GetChildDevicesAsync(token);
            } catch (Exception) {
                // An ordinary single-outlet device (the vast majority of KLAP devices, e.g. the P115)
                // doesn't support get_child_device_list at all - it's expected to error out here, not
                // return an empty list, so this must be treated as "no children" rather than let the
                // exception propagate and take the whole device down (confirmed on real hardware: a
                // P115 disappeared from the plug list entirely before this fix, since the caller in
                // PlugRegistryService excludes a device outright on any exception from this factory).
                children = null;
            }

            if (children == null || children.Count == 0) {
                return new List<KlapPlugDriver> { parentDriver };
            }

            var drivers = new List<KlapPlugDriver>(children.Count);
            for (int i = 0; i < children.Count; i++) {
                var child = children[i];
                string childPlugId = $"{deviceId}:{child.DeviceId}";
                string childAlias = !string.IsNullOrWhiteSpace(child.Nickname) ? child.Nickname : $"{alias} #{i + 1}";
                var childDriver = ReuseOrCreate(childPlugId, childAlias, child.DeviceId);
                // Every outlet on the same physical strip shares one KLAP session - logging in
                // separately per outlet would be exactly the kind of redundant re-authentication
                // CLAUDE.md's gotchas warn about (the parent probe above already logged in once).
                childDriver.AdoptSessionFrom(parentDriver);
                drivers.Add(childDriver);
            }
            return drivers;
        }
    }
}
