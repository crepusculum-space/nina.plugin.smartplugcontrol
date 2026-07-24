using System;
using System.Security.Cryptography;
using System.Text;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud {
    /// <summary>
    /// Encrypts/decrypts the TP-Link cloud password at rest using Windows DPAPI (current-user scope),
    /// so it is never persisted in plain text in the plugin's settings file.
    /// </summary>
    public static class SecureCredentialStore {
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
