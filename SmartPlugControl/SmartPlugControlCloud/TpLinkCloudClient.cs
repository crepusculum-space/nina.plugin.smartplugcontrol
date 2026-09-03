using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TapoConnect;
using TapoConnect.Dto;
using TapoConnect.Exceptions;
using TapoConnect.Util;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    /// <summary>
    /// Wraps TapoConnect's TapoCloudClient, which talks to the shared TP-Link cloud endpoint
    /// (wap.tplinkcloud.com) used by both the Kasa and Tapo apps. A single TP-Link ID login
    /// returns every device on the account regardless of brand; devices are told apart
    /// afterwards by DeviceType (TapoUtils.IsTapoDevice - anything else is treated as Kasa).
    /// This client only covers discovery (login + listing); device control uses a separate,
    /// signed protocol - see KasaCloudPassthroughClient.
    /// </summary>
    public class TpLinkCloudClient : ITpLinkCloudClient {
        private readonly ITapoCloudClient cloudClient;

        public TpLinkCloudClient() : this(new TapoCloudClient()) {
        }

        public TpLinkCloudClient(ITapoCloudClient cloudClient) {
            this.cloudClient = cloudClient;
        }

        // Not one of TapoConnect's own named error codes (TapoException.ThrowFromErrorCode falls
        // through to its generic "Unexpected Error Code: -20601" message for it) - confirmed
        // empirically during testing that TP-Link's cloud API returns this specific code for a wrong
        // username/password, never for a correct login. Translated here so the message shown to the
        // user (Notification.ShowError uses ex.Message) says something a non-technical user can act
        // on, instead of a bare numeric code.
        private const int InvalidCredentialsErrorCode = -20601;

        public async Task<string> LoginAsync(string username, string password, CancellationToken token = default) {
            try {
                var login = await cloudClient.LoginAsync(username, password);
                token.ThrowIfCancellationRequested();
                return login.Token;
            } catch (TapoException ex) when (ex.ErrorCode == InvalidCredentialsErrorCode) {
                throw new InvalidOperationException("Invalid TP-Link username or password.", ex);
            }
        }

        public async Task<IReadOnlyList<CloudPlugDeviceInfo>> ListDevicesAsync(string cloudToken, CancellationToken token = default) {
            var devices = await cloudClient.ListDevicesAsync(cloudToken);
            token.ThrowIfCancellationRequested();

            return devices.DeviceList
                .Select(d => new CloudPlugDeviceInfo {
                    DeviceId = d.DeviceId,
                    Alias = DecodeAliasIfNeeded(d),
                    DeviceType = d.DeviceType,
                    DeviceModel = d.DeviceModel,
                    DeviceMac = d.DeviceMac,
                    Brand = TapoUtils.IsTapoDevice(d.DeviceType) ? PlugBrand.Tapo : PlugBrand.Kasa,
                    IsOnline = d.Status == 1,
                    AppServerUrl = d.AppServerUrl
                })
                .ToList();
        }

        // TP-Link base64-encodes the Alias for every device in the newer "SMART.*" protocol family
        // (confirmed on a real Kasa KP125M, DeviceType "SMART.KASAPLUG"), not just Tapo. TapoConnect's
        // own ListDevicesAsync already decodes it, but only for DeviceTypes TapoUtils.IsTapoDevice
        // recognizes (a narrow exact-match list that predates newer Kasa hardware sharing this
        // protocol) - so a "SMART.KASAPLUG" device is left with its raw base64 string as the alias.
        // Legacy "IOT.*" devices (HS103, KP303) were never affected - their alias comes through as
        // plain text already, so this only applies to the "SMART.*" family the library's own check missed.
        private static string DecodeAliasIfNeeded(TapoDeviceDto d) {
            if (TapoUtils.IsTapoDevice(d.DeviceType) || d.DeviceType == null || !d.DeviceType.StartsWith("SMART.", StringComparison.OrdinalIgnoreCase)) {
                return d.Alias;
            }
            try {
                return TapoCrypto.Base64Decode(d.Alias);
            } catch (FormatException) {
                // Not actually base64 (e.g. a future SMART.* type that isn't encoded after all) - leave as-is.
                return d.Alias;
            }
        }
    }
}
