using System;
using System.Security.Cryptography;
using System.Text;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Encrypts refresh tokens for storage in the settings file with the Windows Data Protection API,
    /// so they can only be read back by the same Windows user.
    /// </summary>
    internal static class TokenProtector
    {
        private static readonly byte[] s_entropy = Encoding.UTF8.GetBytes("EVEMon.ESI.RefreshToken");

        /// <summary>
        /// Encrypts the given token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <returns>The encrypted token, base64 encoded.</returns>
        internal static string Protect(string token)
        {
            if (String.IsNullOrEmpty(token))
                return String.Empty;

            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), s_entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        /// <summary>
        /// Decrypts a token encrypted with <see cref="Protect"/>.
        /// </summary>
        /// <param name="protectedToken">The encrypted token.</param>
        /// <returns>
        /// The token, or null when it cannot be decrypted (e.g. settings copied from another
        /// computer or Windows account, or restored from cloud storage).
        /// </returns>
        internal static string Unprotect(string protectedToken)
        {
            if (String.IsNullOrEmpty(protectedToken))
                return null;

            try
            {
                byte[] decrypted = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), s_entropy,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                return null;
            }
        }
    }
}
