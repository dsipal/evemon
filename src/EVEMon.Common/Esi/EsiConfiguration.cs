using System;
using System.Collections.Generic;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Configuration of EVEMon's EVE SSO application and of the ESI endpoints it uses.
    /// </summary>
    /// <remarks>
    /// EVEMon authenticates characters with EVE SSO (OAuth 2.0 authorization code flow with PKCE),
    /// so the application is registered as a public client and no client secret is needed.
    /// To register it, create an application at https://developers.eveonline.com/ with:
    /// <list type="bullet">
    /// <item>Connection type: Authentication &amp; API Access</item>
    /// <item>Permissions: every scope in <see cref="Scopes"/></item>
    /// <item>Callback URL: <see cref="CallbackUri"/> (http://localhost:52714/callback)</item>
    /// </list>
    /// then put its Client ID in <see cref="DefaultClientId"/>, or set the EVEMON_ESI_CLIENT_ID
    /// environment variable to try another one without rebuilding.
    /// </remarks>
    public static class EsiConfiguration
    {
        /// <summary>
        /// The Client ID of EVEMon's EVE SSO application.
        /// </summary>
        private const string DefaultClientId = "542cf6f8258d494793323cbbc5aa7e70";

        /// <summary>
        /// The local port the SSO login redirects to; it is part of the registered callback URL.
        /// </summary>
        public const int CallbackPort = 52714;

        /// <summary>
        /// The EVE SSO host.
        /// </summary>
        public const string SsoBase = "https://login.eveonline.com";

        /// <summary>
        /// The ESI host, including the route version.
        /// </summary>
        public const string EsiBase = "https://esi.evetech.net/latest";

        /// <summary>
        /// Gets the SSO application's Client ID.
        /// </summary>
        public static string ClientId
        {
            get
            {
                string fromEnvironment = Environment.GetEnvironmentVariable("EVEMON_ESI_CLIENT_ID");
                return String.IsNullOrWhiteSpace(fromEnvironment) ? DefaultClientId : fromEnvironment.Trim();
            }
        }

        /// <summary>
        /// Gets true if a Client ID has been configured.
        /// </summary>
        public static bool IsClientIdConfigured => !String.IsNullOrWhiteSpace(ClientId);

        /// <summary>
        /// Gets the URL EVE SSO redirects to after the login, served by EVEMon itself.
        /// </summary>
        public static Uri CallbackUri { get; } = new Uri($"http://localhost:{CallbackPort}/callback");

        /// <summary>
        /// Gets the scopes requested when a character is authorized.
        /// </summary>
        public static IReadOnlyList<string> Scopes { get; } = new[]
        {
            EsiScopes.ReadSkills,
            EsiScopes.ReadSkillQueue,
            EsiScopes.ReadClones,
            EsiScopes.ReadImplants,
            EsiScopes.ReadCharacterWallet,
            EsiScopes.ReadLocation,
            EsiScopes.ReadShipType,
            EsiScopes.ReadFatigue,
            EsiScopes.ReadStructures,
            EsiScopes.ReadStandings,
            EsiScopes.ReadContacts,
            EsiScopes.ReadCorporationContacts,
            EsiScopes.ReadAllianceContacts,
            EsiScopes.ReadMedals,
            EsiScopes.ReadFactionalWarfareStats,
            EsiScopes.ReadAgentsResearch,
            EsiScopes.ReadCharacterOrders,
            EsiScopes.ReadCharacterJobs,
            EsiScopes.ReadCharacterContracts,
            EsiScopes.ReadAssets,
            EsiScopes.ReadMail,
            EsiScopes.ReadNotifications,
            EsiScopes.ReadKillmails,
            EsiScopes.ManagePlanets,
            EsiScopes.ReadCalendarEvents,
            EsiScopes.ReadCorporationMedals,
            EsiScopes.ReadCorporationOrders,
            EsiScopes.ReadCorporationContracts,
            EsiScopes.ReadCorporationJobs,
        };
    }
}
