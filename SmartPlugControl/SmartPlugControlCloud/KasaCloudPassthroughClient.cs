using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    /// <summary>
    /// Controls Kasa devices entirely through the TP-Link cloud "passthrough" relay, never talking to
    /// the device's local IP directly. This is deliberate: this plugin targets multi-tenant remote
    /// observatories where the network may not be segmented per client, so control must be scoped by
    /// the TP-Link account (enforced server-side by TP-Link) rather than by network reachability.
    ///
    /// Protocol reverse-engineered from https://github.com/piekstra/tplink-cloud-api (signing.py,
    /// device_client.py, device_manager.py): each device's own cloud "appServerUrl" is POSTed a
    /// V1-style {"method":"passthrough","params":{"deviceId","requestData"}} envelope, signed with
    /// V2 HMAC-SHA1 using Kasa's app-identifying (not user-specific) AccessKey/SecretKey pair.
    /// requestData carries the same JSON commands as the legacy local Kasa protocol.
    /// </summary>
    public class KasaCloudPassthroughClient {
        // Kasa app-identifying keys (not user secrets), extracted from the Kasa Android APK by the
        // community. Identical for every user of this protocol - see piekstra/tplink-cloud-api.
        private const string AccessKey = "e37525375f8845999bcc56d5e6faa76d";
        private const string SecretKey = "314bc6700b3140ca80bc655e527cb062";
        private const string SigningTimestamp = "9999999999";
        private const string UrlPath = "/";

        private readonly HttpClient httpClient;

        public KasaCloudPassthroughClient() : this(new HttpClient()) {
        }

        internal KasaCloudPassthroughClient(HttpClient httpClient) {
            this.httpClient = httpClient;
        }

        public async Task SetRelayStateAsync(string appServerUrl, string cloudToken, string deviceId, string childId, bool on, CancellationToken token = default) {
            var command = new JObject {
                ["system"] = new JObject {
                    ["set_relay_state"] = new JObject { ["state"] = on ? 1 : 0 }
                }
            };
            var result = await SendCommandAsync(appServerUrl, cloudToken, deviceId, childId, command, token);
            ThrowIfDeviceError(Unwrap(result, "system", "set_relay_state"));
        }

        public async Task SetLedOffAsync(string appServerUrl, string cloudToken, string deviceId, string childId, bool ledOn, CancellationToken token = default) {
            // Nested under "system" like every other command here - inverted boolean semantics
            // ("off": 1 means the LED is off) per the device's own protocol.
            var command = new JObject {
                ["system"] = new JObject {
                    ["set_led_off"] = new JObject { ["off"] = ledOn ? 0 : 1 }
                }
            };
            var result = await SendCommandAsync(appServerUrl, cloudToken, deviceId, childId, command, token);
            ThrowIfDeviceError(Unwrap(result, "system", "set_led_off"));
        }

        public async Task<JObject> GetRealtimeEnergyAsync(string appServerUrl, string cloudToken, string deviceId, string childId, CancellationToken token = default) {
            var command = new JObject {
                ["emeter"] = new JObject { ["get_realtime"] = JValue.CreateNull() }
            };
            var result = await SendCommandAsync(appServerUrl, cloudToken, deviceId, childId, command, token);
            var emeter = Unwrap(result, "emeter", "get_realtime");
            ThrowIfDeviceError(emeter);
            return emeter;
        }

        /// <summary>
        /// Fetches the device's full system info. For a plug/single-socket outlet this is the
        /// device's own state; for a power strip it also carries the "children" array (each with
        /// "id", "alias", "state") used to enumerate individual sockets.
        /// </summary>
        public async Task<JObject> GetSysInfoAsync(string appServerUrl, string cloudToken, string deviceId, string childId = null, CancellationToken token = default) {
            var command = new JObject {
                ["system"] = new JObject { ["get_sysinfo"] = JValue.CreateNull() }
            };
            var result = await SendCommandAsync(appServerUrl, cloudToken, deviceId, childId, command, token);
            var sysInfo = Unwrap(result, "system", "get_sysinfo");
            ThrowIfDeviceError(sysInfo);

            if (childId != null && sysInfo["children"] is JArray children) {
                var child = children.FirstOrDefault(c => (string)c["id"] == childId) as JObject;
                if (child != null) {
                    return child;
                }
            }
            return sysInfo;
        }

        private static JObject Unwrap(JObject responseData, string requestType, string subRequestType) {
            return responseData?[requestType]?[subRequestType] as JObject;
        }

        private static void ThrowIfDeviceError(JObject deviceResponse) {
            // A missing sub-response means the command shape didn't match what the device expected
            // (e.g. wrong nesting) - treat that the same as an explicit device error rather than
            // silently doing nothing, since the command may look like it "succeeded" with no effect.
            if (deviceResponse == null) {
                throw new KasaCloudException(-1, "device returned no data for this command - the request shape is probably wrong");
            }
            int errCode = deviceResponse.Value<int?>("err_code") ?? 0;
            if (errCode != 0) {
                throw new KasaCloudException(errCode, deviceResponse.Value<string>("err_msg") ?? "device rejected the command");
            }
        }

        public async Task<JObject> SendCommandAsync(string appServerUrl, string cloudToken, string deviceId, string childId, JObject command, CancellationToken token = default) {
            if (childId != null) {
                command["context"] = new JObject { ["child_ids"] = new JArray(childId) };
            }
            string requestDataJson = command.ToString(Newtonsoft.Json.Formatting.None);

            var body = new JObject {
                ["method"] = "passthrough",
                ["params"] = new JObject {
                    ["deviceId"] = deviceId,
                    ["requestData"] = requestDataJson
                }
            };
            string bodyJson = body.ToString(Newtonsoft.Json.Formatting.None);

            byte[] contentMd5Bytes = MD5.HashData(Encoding.UTF8.GetBytes(bodyJson));
            string contentMd5Base64 = Convert.ToBase64String(contentMd5Bytes);
            string nonce = Guid.NewGuid().ToString();
            string sigString = $"{contentMd5Base64}\n{SigningTimestamp}\n{nonce}\n{UrlPath}";
            string signature = ComputeHmacSha1Hex(SecretKey, sigString);
            string authHeader = $"Timestamp={SigningTimestamp}, Nonce={nonce}, AccessKey={AccessKey}, Signature={signature}";

            string url = $"{appServerUrl.TrimEnd('/')}/?token={Uri.EscapeDataString(cloudToken)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url) {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            request.Content.Headers.ContentMD5 = contentMd5Bytes;
            request.Headers.TryAddWithoutValidation("X-Authorization", authHeader);

            using var response = await httpClient.SendAsync(request, token);
            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync(token);
            var responseObj = JObject.Parse(responseJson);

            int errorCode = responseObj.Value<int?>("error_code") ?? 0;
            if (errorCode != 0) {
                throw new KasaCloudException(errorCode, responseObj.Value<string>("msg"));
            }

            string responseDataRaw = responseObj["result"]?.Value<string>("responseData");
            return responseDataRaw != null ? JObject.Parse(responseDataRaw) : null;
        }

        private static string ComputeHmacSha1Hex(string secretKey, string message) {
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secretKey));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
