using System;
using System.Security.Cryptography;
using System.Text;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    /// <summary>
    /// Encrypts/decrypts the TP-Link cloud password at rest using Windows DPAPI (current-user scope),
    /// so it is never persisted in plain text in the plugin's settings file.
    /// </summary>
    public static class SecureCredentialStore {
        private static bool? isAvailable;

        /// <summary>
        /// Whether Windows DPAPI is actually usable on this machine/user profile, checked once via a
        /// real encrypt/decrypt round-trip (not just whether the type/assembly loads) - DPAPI itself
        /// can fail for reasons beyond a missing assembly, e.g. a temporary or roaming Windows profile
        /// without usable key material. Cached after the first call since this can't change mid-session.
        /// </summary>
        public static bool IsAvailable() {
            if (isAvailable == null) {
                try {
                    isAvailable = Unprotect(Protect("smart-plug-control-dpapi-check")) == "smart-plug-control-dpapi-check";
                } catch (Exception) {
                    isAvailable = false;
                }
            }
            return isAvailable.Value;
        }

        public static string Protect(string plainText) {
            if (string.IsNullOrEmpty(plainText)) {
                return string.Empty;
            }
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Unprotect(string protectedText) {
            if (string.IsNullOrEmpty(protectedText)) {
                return string.Empty;
            }
            try {
                byte[] protectedBytes = Convert.FromBase64String(protectedText);
                byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            } catch (Exception) {
                // Stored value was corrupted, empty, or protected under a different user profile - treat as unset.
                return string.Empty;
            }
        }
    }
}
