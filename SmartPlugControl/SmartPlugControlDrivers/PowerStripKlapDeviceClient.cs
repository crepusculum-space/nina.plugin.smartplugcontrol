using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TapoConnect;
using TapoConnect.Dto;
using TapoConnect.Exceptions;
using TapoConnect.Protocol;
using TapoConnect.Util;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// Extends TapoConnect's KlapDeviceClient with the two "child device" commands
    /// (get_child_device_list, control_child) it doesn't implement - needed for Tapo multi-outlet
    /// power strips (e.g. the P300 family/P316M), which the base client only ever sees as a single
    /// logical device with one on/off state (confirmed by reading TapoConnect's DeviceGetInfoResult -
    /// it has no children/child_devices field at all). Built entirely on TapoConnect's own protected
    /// KlapRequestAsync - the handshake/session/cipher are reused completely unchanged, only the two
    /// extra request shapes below are new.
    ///
    /// Request/response shapes ported from python-kasa (GPL-3.0-or-later:
    /// kasa/smart/modules/childdevice.py and kasa/protocols/smartprotocol.py's _ChildProtocolWrapper),
    /// cross-checked against a real P300 device capture in python-kasa's own test fixtures
    /// (tests/fixtures/smart/P300(EU)_*.json). GPL-3.0-or-later is compatible with this plugin's own
    /// GPL-3.0-only license - see CLAUDE.md's GPL-3.0 relicense section for why that distinction
    /// matters here.
    /// </summary>
    public class PowerStripKlapDeviceClient : KlapDeviceClient {
        public PowerStripKlapDeviceClient(JsonSerializerOptions jsonSerializerOptions = null) : base(jsonSerializerOptions) {
        }

        public async Task<List<DeviceGetInfoResult>> GetChildDeviceListAsync(TapoDeviceKey deviceKey) {
            if (deviceKey == null) {
                throw new ArgumentNullException(nameof(deviceKey));
            }

            var protocol = deviceKey.ToProtocol<KlapDeviceKey>();

            var request = new TapoRequest {
                Method = "get_child_device_list",
            };

            var jsonRequest = JsonSerializer.Serialize(request);

            var response = await KlapRequestAsync<ChildDeviceListResponse>(jsonRequest, deviceKey.DeviceIp, deviceKey.SessionCookie, protocol.KlapChiper);

            // Same alias-is-base64-encoded quirk already handled for the parent device in
            // KlapDeviceClient.GetDeviceInfoAsync (TapoConnect decodes Ssid/Nickname there but has no
            // equivalent code path for child entries, since it doesn't know children exist at all).
            foreach (var child in response.Result.ChildDeviceList) {
                child.Nickname = TapoCrypto.Base64Decode(child.Nickname);
            }

            return response.Result.ChildDeviceList;
        }

        public async Task SetChildPowerAsync(TapoDeviceKey deviceKey, string childDeviceId, bool on) {
            if (deviceKey == null) {
                throw new ArgumentNullException(nameof(deviceKey));
            }
            if (string.IsNullOrEmpty(childDeviceId)) {
                throw new ArgumentNullException(nameof(childDeviceId));
            }

            var protocol = deviceKey.ToProtocol<KlapDeviceKey>();

            var innerRequest = new TapoRequest<TapoSetBulbState> {
                Method = "set_device_info",
                Params = new TapoSetBulbState(on),
            };

            var request = new TapoRequest<ControlChildParams> {
                Method = "control_child",
                Params = new ControlChildParams {
                    DeviceId = childDeviceId,
                    RequestData = innerRequest,
                },
            };

            var jsonRequest = JsonSerializer.Serialize(request);

            var response = await KlapRequestAsync<ControlChildResponse>(jsonRequest, deviceKey.DeviceIp, deviceKey.SessionCookie, protocol.KlapChiper);

            // KlapRequestAsync already checks the outer control_child envelope's own error_code before
            // returning here (throws if non-zero) - but the wrapped inner command (set_device_info) has
            // its own, separate error_code nested under responseData, which KlapRequestAsync has no way
            // to know about since it's specific to this control_child shape, so it's checked explicitly.
            TapoException.ThrowFromErrorCode(response.Result.ResponseData.ErrorCode);
        }

        /// <summary>
        /// Per-outlet consumption, for power strip models that support it (confirmed on a real P316M
        /// capture in python-kasa's own test fixtures - each child declares its own "energy_monitoring"
        /// component, separate from the same capability at the whole-strip level; the base P300 has no
        /// energy capability at all, at either level). Throws (via TapoException.ThrowFromErrorCode) if
        /// this particular outlet doesn't support it - callers should treat that the same as any other
        /// KLAP device without an energy meter (KlapPlugDriver already does, for the plain single-device
        /// case).
        /// </summary>
        public async Task<DeviceGetEnergyUsageResult> GetChildEnergyUsageAsync(TapoDeviceKey deviceKey, string childDeviceId) {
            if (deviceKey == null) {
                throw new ArgumentNullException(nameof(deviceKey));
            }
            if (string.IsNullOrEmpty(childDeviceId)) {
                throw new ArgumentNullException(nameof(childDeviceId));
            }

            var protocol = deviceKey.ToProtocol<KlapDeviceKey>();

            var innerRequest = new TapoRequest {
                Method = "get_energy_usage",
            };

            var request = new TapoRequest<ControlChildParams> {
                Method = "control_child",
                Params = new ControlChildParams {
                    DeviceId = childDeviceId,
                    RequestData = innerRequest,
                },
            };

            var jsonRequest = JsonSerializer.Serialize(request);

            var response = await KlapRequestAsync<ControlChildEnergyResponse>(jsonRequest, deviceKey.DeviceIp, deviceKey.SessionCookie, protocol.KlapChiper);

            TapoException.ThrowFromErrorCode(response.Result.ResponseData.ErrorCode);

            return response.Result.ResponseData.Result;
        }
    }

    public class ChildDeviceListResponse : TapoResponse<ChildDeviceListResult> { }

    public class ChildDeviceListResult {
        [JsonPropertyName("child_device_list")]
        public List<DeviceGetInfoResult> ChildDeviceList { get; set; } = null!;
    }

    public class ControlChildParams {
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = null!;

        [JsonPropertyName("requestData")]
        public object RequestData { get; set; } = null!;
    }

    public class ControlChildResponse : TapoResponse<ControlChildResult> { }

    public class ControlChildResult {
        [JsonPropertyName("responseData")]
        public TapoResponse ResponseData { get; set; } = null!;
    }

    public class ControlChildEnergyResponse : TapoResponse<ControlChildEnergyResult> { }

    public class ControlChildEnergyResult {
        // DeviceGetEnergyUsageResponse is TapoConnect's own existing concrete DTO for the plain
        // (non-control_child) get_energy_usage response - reused as-is, since a control_child
        // envelope's responseData has exactly the same {error_code, result} shape as a normal response.
        [JsonPropertyName("responseData")]
        public DeviceGetEnergyUsageResponse ResponseData { get; set; } = null!;
    }
}
