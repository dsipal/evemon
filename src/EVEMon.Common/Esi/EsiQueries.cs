using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EVEMon.Common.Enumerations;
using EVEMon.Common.Enumerations.CCPAPI;
using EVEMon.Common.Helpers;
using EVEMon.Common.Models;
using EVEMon.Common.Net;
using EVEMon.Common.Serialization.Eve;
using EVEMon.Common.Threading;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// The data queries EVEMon can run on ESI, keyed by the XML API method they replace.
    /// </summary>
    /// <remarks>
    /// Methods without an entry here are not available on ESI yet; their monitors report no access,
    /// which hides the related features in the UI.
    /// </remarks>
    public static class EsiQueries
    {
        private static readonly Dictionary<Enum, EsiQueryDefinition> s_definitions = new Dictionary<Enum, EsiQueryDefinition>
        {
            [CCPAPICharacterMethods.CharacterSheet] = new EsiQueryDefinition(EsiCharacterQueries.CharacterSheetAsync,
                EsiScopes.ReadSkills, EsiScopes.ReadImplants, EsiScopes.ReadClones, EsiScopes.ReadCharacterWallet,
                EsiScopes.ReadFatigue) { KeepXml = true },
            [CCPAPICharacterMethods.CharacterInfo] = new EsiQueryDefinition(EsiCharacterQueries.CharacterInfoAsync,
                EsiScopes.ReadLocation, EsiScopes.ReadShipType),
            [CCPAPICharacterMethods.SkillQueue] = new EsiQueryDefinition(EsiCharacterQueries.SkillQueueAsync,
                EsiScopes.ReadSkillQueue),
            [CCPAPICharacterMethods.Standings] = new EsiQueryDefinition(EsiCharacterQueries.StandingsAsync,
                EsiScopes.ReadStandings),
            [CCPAPICharacterMethods.ContactList] = new EsiQueryDefinition(EsiCharacterQueries.ContactsAsync,
                EsiScopes.ReadContacts),
            [CCPAPICharacterMethods.Medals] = new EsiQueryDefinition(EsiCharacterQueries.MedalsAsync,
                EsiScopes.ReadMedals),
            [CCPAPICharacterMethods.FactionalWarfareStats] = new EsiQueryDefinition(
                EsiCharacterQueries.FactionalWarfareStatsAsync, EsiScopes.ReadFactionalWarfareStats),
            [CCPAPICharacterMethods.ResearchPoints] = new EsiQueryDefinition(EsiCharacterQueries.ResearchPointsAsync,
                EsiScopes.ReadAgentsResearch),
            [CCPAPICharacterMethods.WalletJournal] = new EsiQueryDefinition(EsiCharacterQueries.WalletJournalAsync,
                EsiScopes.ReadCharacterWallet),
            [CCPAPICharacterMethods.WalletTransactions] = new EsiQueryDefinition(
                EsiCharacterQueries.WalletTransactionsAsync, EsiScopes.ReadCharacterWallet),
            [CCPAPICharacterMethods.MarketOrders] = new EsiQueryDefinition(EsiCharacterQueries.MarketOrdersAsync,
                EsiScopes.ReadCharacterOrders),
            [CCPAPICharacterMethods.IndustryJobs] = new EsiQueryDefinition(EsiCharacterQueries.IndustryJobsAsync,
                EsiScopes.ReadCharacterJobs),
            [CCPAPICharacterMethods.Contracts] = new EsiQueryDefinition(EsiCharacterQueries.ContractsAsync,
                EsiScopes.ReadCharacterContracts),
            [CCPAPIGenericMethods.ContractBids] = new EsiQueryDefinition(EsiCharacterQueries.ContractBidsAsync,
                EsiScopes.ReadCharacterContracts),
            [CCPAPIGenericMethods.ContractItems] = new EsiQueryDefinition(EsiCharacterQueries.ContractItemsAsync,
                EsiScopes.ReadCharacterContracts),
            [CCPAPICharacterMethods.AssetList] = new EsiQueryDefinition(EsiCharacterQueries.AssetsAsync,
                EsiScopes.ReadAssets),
            [CCPAPICharacterMethods.MailMessages] = new EsiQueryDefinition(EsiCharacterQueries.MailMessagesAsync,
                EsiScopes.ReadMail),
            [CCPAPICharacterMethods.MailingLists] = new EsiQueryDefinition(EsiCharacterQueries.MailingListsAsync,
                EsiScopes.ReadMail),
            [CCPAPICharacterMethods.MailBodies] = new EsiQueryDefinition(EsiCharacterQueries.MailBodyAsync,
                EsiScopes.ReadMail),
            [CCPAPICharacterMethods.Notifications] = new EsiQueryDefinition(EsiCharacterQueries.NotificationsAsync,
                EsiScopes.ReadNotifications),
            [CCPAPICharacterMethods.NotificationTexts] = new EsiQueryDefinition(
                EsiCharacterQueries.NotificationTextAsync, EsiScopes.ReadNotifications),
            [CCPAPICharacterMethods.KillLog] = new EsiQueryDefinition(EsiCharacterQueries.KillLogAsync,
                EsiScopes.ReadKillmails) { KeepXml = true },
            [CCPAPIGenericMethods.PlanetaryColonies] = new EsiQueryDefinition(
                EsiCharacterQueries.PlanetaryColoniesAsync, EsiScopes.ManagePlanets),
            [CCPAPIGenericMethods.PlanetaryPins] = new EsiQueryDefinition(EsiCharacterQueries.PlanetaryPinsAsync,
                EsiScopes.ManagePlanets),
            [CCPAPIGenericMethods.PlanetaryRoutes] = new EsiQueryDefinition(EsiCharacterQueries.PlanetaryRoutesAsync,
                EsiScopes.ManagePlanets),
            [CCPAPIGenericMethods.PlanetaryLinks] = new EsiQueryDefinition(EsiCharacterQueries.PlanetaryLinksAsync,
                EsiScopes.ManagePlanets),
            [CCPAPICharacterMethods.UpcomingCalendarEvents] = new EsiQueryDefinition(
                EsiCharacterQueries.UpcomingCalendarEventsAsync, EsiScopes.ReadCalendarEvents),
            [CCPAPICharacterMethods.CalendarEventAttendees] = new EsiQueryDefinition(
                EsiCharacterQueries.CalendarEventAttendeesAsync, EsiScopes.ReadCalendarEvents),
            [CCPAPICorporationMethods.CorporationMedals] = new EsiQueryDefinition(
                EsiCharacterQueries.CorporationMedalsAsync, EsiScopes.ReadCorporationMedals),
            [CCPAPICorporationMethods.CorporationMarketOrders] = new EsiQueryDefinition(
                EsiCharacterQueries.CorporationMarketOrdersAsync, EsiScopes.ReadCorporationOrders),
            [CCPAPICorporationMethods.CorporationContracts] = new EsiQueryDefinition(
                EsiCharacterQueries.CorporationContractsAsync, EsiScopes.ReadCorporationContracts),
            [CCPAPIGenericMethods.CorporationContractBids] = new EsiQueryDefinition(
                EsiCharacterQueries.CorporationContractBidsAsync, EsiScopes.ReadCorporationContracts),
            [CCPAPIGenericMethods.CorporationContractItems] = new EsiQueryDefinition(
                EsiCharacterQueries.CorporationContractItemsAsync, EsiScopes.ReadCorporationContracts),
            [CCPAPICorporationMethods.CorporationIndustryJobs] = new EsiQueryDefinition(
                EsiCharacterQueries.CorporationIndustryJobsAsync, EsiScopes.ReadCorporationJobs),
        };

        /// <summary>
        /// Gets true if the given method has an ESI implementation.
        /// </summary>
        /// <param name="method">The method.</param>
        public static bool IsSupported(Enum method) => method != null && s_definitions.ContainsKey(method);

        /// <summary>
        /// Gets true if the given key can be used to query the given method.
        /// </summary>
        /// <param name="esiKey">The ESI key.</param>
        /// <param name="method">The method.</param>
        internal static bool HasAccess(ESIKey esiKey, Enum method)
        {
            EsiQueryDefinition definition;
            return esiKey != null && esiKey.Monitored && esiKey.State != ESIKeyState.NeedsAuthorization &&
                   method != null && s_definitions.TryGetValue(method, out definition) &&
                   definition.Scopes.All(esiKey.HasScope);
        }

        /// <summary>
        /// Asynchronously queries the given method; the callback is invoked on the UI thread.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="method">The method.</param>
        /// <param name="esiKey">The ESI key of the character.</param>
        /// <param name="characterID">The character ID.</param>
        /// <param name="callback">The callback.</param>
        internal static void QueryAsync<T>(Enum method, ESIKey esiKey, long characterID, Action<CCPAPIResult<T>> callback)
            => QueryAsync(method, esiKey, characterID, 0, callback);

        /// <summary>
        /// Asynchronously queries the given method for a specific item (mail, contract, planet...);
        /// the callback is invoked on the UI thread.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="method">The method.</param>
        /// <param name="esiKey">The ESI key of the character.</param>
        /// <param name="characterID">The character ID.</param>
        /// <param name="id">The ID of the item.</param>
        /// <param name="callback">The callback.</param>
        internal static void QueryAsync<T>(Enum method, ESIKey esiKey, long characterID, long id,
            Action<CCPAPIResult<T>> callback)
        {
            Task.Run(async () =>
            {
                CCPAPIResult<T> result = await QueryCoreAsync<T>(method, esiKey, characterID, id).ConfigureAwait(false);
                Dispatcher.Invoke(() => callback(result));
            });
        }

        private static async Task<CCPAPIResult<T>> QueryCoreAsync<T>(Enum method, ESIKey esiKey, long characterID, long id)
        {
            EsiQueryDefinition definition;
            if (!s_definitions.TryGetValue(method, out definition))
                return new CCPAPIResult<T>(CCPAPIErrors.Http, $"{method} is not available on ESI yet.");

            try
            {
                string accessToken = await esiKey.GetAccessTokenAsync().ConfigureAwait(false);
                EsiQueryResult queryResult = await definition.Query(new EsiQueryContext(accessToken, characterID, id))
                    .ConfigureAwait(false);

                CCPAPIResult<T> result = new CCPAPIResult<T>
                {
                    Result = (T)queryResult.Result,
                    CachedUntil = queryResult.Expires,
                    CurrentTime = queryResult.ServerTime,
                    CCPError = queryResult.Error
                };

                // Compensate for the difference between the server and the local clock
                result.SynchronizeWithLocalClock(queryResult.ServerTime.Subtract(DateTime.UtcNow).TotalMilliseconds);

                // Some results are saved to the local cache in the XML API format
                if (definition.KeepXml && result.Result != null)
                    result.XmlDocument = Util.SerializeToXmlDocument(result);

                return result;
            }
            catch (HttpWebClientServiceException ex)
            {
                return new CCPAPIResult<T>(ex);
            }
            catch (SsoException ex)
            {
                return new CCPAPIResult<T>(CCPAPIErrors.Http, ex.Message);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                ExceptionHandler.LogException(ex, true);
                return new CCPAPIResult<T>(CCPAPIErrors.Http, $"The ESI data could not be read: {ex.Message}");
            }
        }

        #region Nested types

        private sealed class EsiQueryDefinition
        {
            internal EsiQueryDefinition(Func<EsiQueryContext, Task<EsiQueryResult>> query, params string[] scopes)
            {
                Query = query;
                Scopes = scopes;
            }

            internal Func<EsiQueryContext, Task<EsiQueryResult>> Query { get; }

            internal IReadOnlyCollection<string> Scopes { get; }

            /// <summary>
            /// Gets or sets whether the result is also serialized to XML, for the methods saved to the local cache.
            /// </summary>
            internal bool KeepXml { get; set; }
        }

        #endregion
    }

    /// <summary>
    /// The parameters of an ESI query.
    /// </summary>
    internal sealed class EsiQueryContext
    {
        internal EsiQueryContext(string accessToken, long characterID, long id)
        {
            AccessToken = accessToken;
            CharacterID = characterID;
            ID = id;
        }

        internal string AccessToken { get; }

        internal long CharacterID { get; }

        /// <summary>
        /// Gets the ID of the queried item (mail, contract, planet...), or 0.
        /// </summary>
        internal long ID { get; }
    }

    /// <summary>
    /// The result of an ESI query, with ESI's cache expiry and server time.
    /// </summary>
    internal sealed class EsiQueryResult
    {
        internal EsiQueryResult(object result, DateTime expires, DateTime serverTime, CCPAPIError error = null)
        {
            Result = result;
            Expires = expires;
            ServerTime = serverTime;
            Error = error;
        }

        internal object Result { get; }

        internal DateTime Expires { get; }

        internal DateTime ServerTime { get; }

        /// <summary>
        /// Gets an XML API error to report instead of the result, for the cases the model handles specially
        /// (e.g. not enlisted in factional warfare).
        /// </summary>
        internal CCPAPIError Error { get; }
    }
}
