using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EVEMon.Common.Attributes;
using EVEMon.Common.Enumerations;
using EVEMon.Common.Esi;
using EVEMon.Common.Net;
using EVEMon.Common.Serialization.Settings;
using EVEMon.Common.Threading;

namespace EVEMon.Common.Models
{
    /// <summary>
    /// Represents a character authorized through EVE SSO, giving access to its data on ESI.
    /// </summary>
    /// <remarks>
    /// Replaces the XML API keys (key ID + verification code) retired by CCP: there is one ESI key
    /// per character, holding the refresh token EVE SSO issued for it and the scopes it granted.
    /// </remarks>
    [EnforceUIThreadAffinity]
    public sealed class ESIKey
    {
        #region Fields

        private readonly SemaphoreSlim m_tokenLock = new SemaphoreSlim(1, 1);

        private string m_refreshToken;
        private string m_accessToken;
        private DateTime m_accessTokenExpires;
        private bool m_monitored;
        private bool m_validated;
        private bool m_validating;

        #endregion


        #region Constructors

        /// <summary>
        /// Deserialization constructor.
        /// </summary>
        /// <param name="serial">The serialized key.</param>
        internal ESIKey(SerializableESIKey serial)
        {
            ID = serial.ID;
            CharacterName = serial.Name ?? String.Empty;
            OwnerHash = serial.OwnerHash ?? String.Empty;
            Scopes = (serial.Scopes ?? String.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            m_monitored = serial.Monitored;
            m_refreshToken = TokenProtector.Unprotect(serial.RefreshToken);

            // The token can't be read back when the settings come from another computer or Windows account
            if (m_refreshToken == null)
            {
                State = ESIKeyState.NeedsAuthorization;
                LastError = "The saved EVE SSO authorization can't be used on this computer.";
                m_validated = true;
            }

            EveMonClient.TimerTick += EveMonClient_TimerTick;
        }

        /// <summary>
        /// Constructor for a newly authorized character.
        /// </summary>
        /// <param name="token">The tokens returned by EVE SSO.</param>
        internal ESIKey(SsoToken token)
        {
            ID = token.CharacterID;
            m_monitored = true;
            ApplyToken(token);
            State = ESIKeyState.Valid;
            m_validated = true;

            EveMonClient.TimerTick += EveMonClient_TimerTick;
        }

        #endregion


        #region Properties

        /// <summary>
        /// Gets the ID of the authorized character.
        /// </summary>
        public long ID { get; }

        /// <summary>
        /// Gets the name of the authorized character.
        /// </summary>
        public string CharacterName { get; private set; }

        /// <summary>
        /// Gets the owner hash of the authorized character (changes when it is transferred).
        /// </summary>
        public string OwnerHash { get; private set; }

        /// <summary>
        /// Gets the scopes granted to EVEMon.
        /// </summary>
        public IReadOnlyCollection<string> Scopes { get; private set; }

        /// <summary>
        /// Gets the state of the authorization.
        /// </summary>
        public ESIKeyState State { get; private set; }

        /// <summary>
        /// Gets the last error message, if any.
        /// </summary>
        public string LastError { get; private set; }

        /// <summary>
        /// Gets true if some of the scopes EVEMon asks for have not been granted
        /// (e.g. a newer EVEMon version needs more), meaning the character should be authorized again.
        /// </summary>
        public bool HasMissingScopes => EsiConfiguration.Scopes.Any(scope => !Scopes.Contains(scope));

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="ESIKey"/> is monitored.
        /// </summary>
        public bool Monitored
        {
            get { return m_monitored; }
            set
            {
                m_monitored = value;
                EveMonClient.OnESIKeyMonitoredChanged();
            }
        }

        /// <summary>
        /// Gets the identity of the authorized character.
        /// </summary>
        public CharacterIdentity CharacterIdentity => EveMonClient.CharacterIdentities[ID];

        /// <summary>
        /// Gets true if the authorized character is monitored.
        /// </summary>
        public bool HasMonitoredCharacters => CharacterIdentity?.CCPCharacter?.Monitored == true;

        /// <summary>
        /// Gets true if this key has been checked at least once, or is not monitored.
        /// </summary>
        public bool IsProcessed => m_validated || !m_monitored;

        #endregion


        #region Methods

        /// <summary>
        /// Gets true if the given scope has been granted.
        /// </summary>
        /// <param name="scope">The scope.</param>
        public bool HasScope(string scope) => Scopes.Contains(scope);

        /// <summary>
        /// Gets a valid access token, refreshing it with EVE SSO when needed.
        /// </summary>
        /// <returns>The access token.</returns>
        /// <exception cref="SsoException">The token could not be refreshed.</exception>
        internal async Task<string> GetAccessTokenAsync()
        {
            await m_tokenLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_accessToken != null && DateTime.UtcNow < m_accessTokenExpires)
                    return m_accessToken;

                if (m_refreshToken == null)
                    throw new SsoException($"{CharacterName} must be authorized again with EVE SSO.", true);

                SsoToken token;
                try
                {
                    token = await SsoClient.RefreshAsync(m_refreshToken).ConfigureAwait(false);
                }
                catch (SsoException ex) when (ex.AuthorizationRevoked)
                {
                    Dispatcher.Invoke(() => SetNeedsAuthorization(ex.Message));
                    throw;
                }

                if (token.CharacterID != ID)
                    throw new SsoException($"EVE SSO returned a token for another character than {CharacterName}.");

                ApplyToken(token);

                // EVE SSO rotates refresh tokens, so the new one must be saved
                Settings.Save();

                return m_accessToken;
            }
            finally
            {
                m_tokenLock.Release();
            }
        }

        /// <summary>
        /// Updates this key with a new EVE SSO authorization of the same character.
        /// </summary>
        /// <param name="token">The tokens returned by EVE SSO.</param>
        internal void Update(SsoToken token)
        {
            if (token.CharacterID != ID)
                throw new ArgumentException("The token is for another character.", nameof(token));

            ApplyToken(token);
            State = ESIKeyState.Valid;
            LastError = null;
            m_validated = true;

            EveMonClient.Notifications.InvalidateESIKeyError(this);
            LinkIdentity();
            EveMonClient.OnESIKeyInfoUpdated(this);
        }

        /// <summary>
        /// Checks the authorization again on the next timer tick.
        /// </summary>
        public void ForceUpdate()
        {
            if (State != ESIKeyState.NeedsAuthorization)
                m_validated = false;
        }

        /// <summary>
        /// Attaches this key to the identity of its character, creating the identity if needed.
        /// </summary>
        internal void LinkIdentity()
        {
            CharacterIdentity identity = EveMonClient.CharacterIdentities[ID] ??
                                         EveMonClient.CharacterIdentities.Add(ID, CharacterName);
            identity.ESIKey = this;

            if (!String.IsNullOrEmpty(CharacterName))
                identity.CharacterName = CharacterName;
        }

        /// <summary>
        /// Called when the object gets disposed.
        /// </summary>
        internal void Dispose()
        {
            EveMonClient.TimerTick -= EveMonClient_TimerTick;
        }

        /// <summary>
        /// Exports the data to a serialization object.
        /// </summary>
        /// <returns></returns>
        internal SerializableESIKey Export()
            => new SerializableESIKey
            {
                ID = ID,
                Name = CharacterName,
                OwnerHash = OwnerHash,
                Scopes = String.Join(" ", Scopes),
                RefreshToken = TokenProtector.Protect(m_refreshToken),
                Monitored = m_monitored
            };

        /// <summary>
        /// Gets a string representation of this key, e.g. "John Doe (90000001)".
        /// </summary>
        public override string ToString() => $"{CharacterName} ({ID})";

        #endregion


        #region Helpers

        private void ApplyToken(SsoToken token)
        {
            m_accessToken = token.AccessToken;
            m_accessTokenExpires = token.AccessTokenExpires;
            m_refreshToken = token.RefreshToken;
            CharacterName = token.CharacterName;
            OwnerHash = token.OwnerHash;
            Scopes = token.Scopes;
        }

        private void SetNeedsAuthorization(string message)
        {
            m_refreshToken = null;
            m_accessToken = null;
            State = ESIKeyState.NeedsAuthorization;
            LastError = message;

            EveMonClient.Notifications.NotifyESIKeyError(this, message);
            EveMonClient.OnESIKeyInfoUpdated(this);
        }

        /// <summary>
        /// Checks the authorization once after loading, so the user learns early about revoked tokens.
        /// </summary>
        private async void EveMonClient_TimerTick(object sender, EventArgs e)
        {
            if (!m_monitored || m_validated || m_validating)
                return;

            // Without network we can't tell; let the queries retry later
            if (!NetworkMonitor.IsNetworkAvailable)
            {
                m_validated = true;
                LinkIdentity();
                EveMonClient.OnESIKeyInfoUpdated(this);
                return;
            }

            m_validating = true;
            try
            {
                // Forces a refresh, which checks the authorization is still valid
                m_accessToken = null;
                await GetAccessTokenAsync();

                State = ESIKeyState.Valid;
                LastError = null;
                EveMonClient.Notifications.InvalidateESIKeyError(this);
            }
            catch (SsoException ex)
            {
                if (!ex.AuthorizationRevoked)
                {
                    State = ESIKeyState.Error;
                    LastError = ex.Message;
                    EveMonClient.Notifications.NotifyESIKeyError(this, ex.Message);
                }
            }
            finally
            {
                m_validating = false;
                m_validated = true;
            }

            // Quit if the key was removed while it was validating
            if (!EveMonClient.ESIKeys.Contains(this))
                return;

            LinkIdentity();
            EveMonClient.OnESIKeyInfoUpdated(this);
        }

        #endregion
    }
}
