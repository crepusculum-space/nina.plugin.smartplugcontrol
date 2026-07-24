using System;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    public class KasaCloudException : Exception {
        public int ErrorCode { get; }

        public KasaCloudException(int errorCode, string message) : base($"Kasa cloud error {errorCode}: {message}") {
            ErrorCode = errorCode;
        }
    }
}
