using System;
using System.Collections.Generic;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// The tokens and character details returned by EVE SSO.
    /// </summary>
    public sealed class SsoToken
    {
        internal SsoToken(string accessToken, string refreshToken, DateTime accessTokenExpires, long characterID,
            string characterName, string ownerHash, IReadOnlyCollection<string> scopes)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            AccessTokenExpires = accessTokenExpires;
            CharacterID = characterID;
            CharacterName = characterName;
            OwnerHash = ownerHash;
            Scopes = scopes;
        }

        /// <summary>
        /// Gets the access token (a JWT, valid for about 20 minutes).
        /// </summary>
        public string AccessToken { get; }

        /// <summary>
        /// Gets the refresh token, used to get new access tokens.
        /// </summary>
        public string RefreshToken { get; }

        /// <summary>
        /// Gets when the access token expires (UTC).
        /// </summary>
        public DateTime AccessTokenExpires { get; }

        /// <summary>
        /// Gets the authorized character's ID.
        /// </summary>
        public long CharacterID { get; }

        /// <summary>
        /// Gets the authorized character's name.
        /// </summary>
        public string CharacterName { get; }

        /// <summary>
        /// Gets the owner hash, which changes when the character is transferred to another account.
        /// </summary>
        public string OwnerHash { get; }

        /// <summary>
        /// Gets the granted scopes.
        /// </summary>
        public IReadOnlyCollection<string> Scopes { get; }
    }
}
