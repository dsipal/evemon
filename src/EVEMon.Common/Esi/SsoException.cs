using System;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Represents a failed EVE SSO authorization or token refresh.
    /// </summary>
    public sealed class SsoException : Exception
    {
        public SsoException()
        {
        }

        public SsoException(string message)
            : base(message)
        {
        }

        public SsoException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        internal SsoException(string message, bool authorizationRevoked, Exception innerException = null)
            : base(message, innerException)
        {
            AuthorizationRevoked = authorizationRevoked;
        }

        /// <summary>
        /// Gets true when the refresh token is no longer valid (revoked, expired or for another client),
        /// meaning the character has to be authorized again.
        /// </summary>
        public bool AuthorizationRevoked { get; }
    }
}
