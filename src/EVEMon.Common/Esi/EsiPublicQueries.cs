using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EVEMon.Common.Helpers;
using EVEMon.Common.Net;
using EVEMon.Common.Serialization.Eve;
using EVEMon.Common.Threading;
using Newtonsoft.Json;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Queries of public ESI data used by EVEMon's global services.
    /// </summary>
    internal static class EsiPublicQueries
    {
        /// <summary>
        /// Asynchronously runs a public query; the callback is invoked on the UI thread with a result in the
        /// XML API format (including its XML document, for the local cache).
        /// </summary>
        internal static void QueryAsync<T>(Func<Task<EsiQueryResult>> query, Action<CCPAPIResult<T>> callback)
        {
            Task.Run(async () =>
            {
                CCPAPIResult<T> result;
                try
                {
                    EsiQueryResult queryResult = await query().ConfigureAwait(false);
                    result = new CCPAPIResult<T>
                    {
                        Result = (T)queryResult.Result,
                        CachedUntil = queryResult.Expires,
                        CurrentTime = queryResult.ServerTime
                    };
                    result.XmlDocument = Util.SerializeToXmlDocument(result);
                }
                catch (HttpWebClientServiceException ex)
                {
                    result = new CCPAPIResult<T>(ex);
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    ExceptionHandler.LogException(ex, true);
                    result = new CCPAPIResult<T>(Enumerations.CCPAPI.CCPAPIErrors.Http,
                        $"The ESI data could not be read: {ex.Message}");
                }

                Dispatcher.Invoke(() => callback(result));
            });
        }

        /// <summary>
        /// Factional warfare statistics of every faction, their totals and the wars between factions.
        /// </summary>
        internal static async Task<EsiQueryResult> FactionalWarfareStatsAsync()
        {
            Task<EsiResponse<List<EsiFactionStats>>> statsTask = EsiClient.GetAsync<List<EsiFactionStats>>("/fw/stats/");
            Task<EsiResponse<List<EsiFactionWar>>> warsTask = EsiClient.GetAsync<List<EsiFactionWar>>("/fw/wars/");
            await Task.WhenAll(statsTask, warsTask).ConfigureAwait(false);

            List<EsiFactionStats> stats = statsTask.Result.Result;
            List<EsiFactionWar> wars = warsTask.Result.Result;

            IDictionary<long, string> names = await EsiNameResolver
                .GetNamesAsync(stats.Select(x => (long)x.FactionID).Concat(wars.Select(x => (long)x.AgainstID)))
                .ConfigureAwait(false);

            SerializableAPIEveFactionalWarfareStats result = new SerializableAPIEveFactionalWarfareStats
            {
                Totals = new SerializableEveFacWarfareTotals
                {
                    KillsYesterday = stats.Sum(x => x.Kills.Yesterday),
                    KillsLastWeek = stats.Sum(x => x.Kills.LastWeek),
                    KillsTotal = stats.Sum(x => x.Kills.Total),
                    VictoryPointsYesterday = stats.Sum(x => x.VictoryPoints.Yesterday),
                    VictoryPointsLastWeek = stats.Sum(x => x.VictoryPoints.LastWeek),
                    VictoryPointsTotal = stats.Sum(x => x.VictoryPoints.Total)
                }
            };

            foreach (EsiFactionStats faction in stats)
            {
                result.FactionalWarfareStats.Add(new SerializableEveFactionalWarfareStatsListItem
                {
                    FactionID = faction.FactionID,
                    FactionName = GetName(names, faction.FactionID),
                    Pilots = faction.Pilots,
                    SystemsControlled = faction.SystemsControlled,
                    KillsYesterday = faction.Kills.Yesterday,
                    KillsLastWeek = faction.Kills.LastWeek,
                    KillsTotal = faction.Kills.Total,
                    VictoryPointsYesterday = faction.VictoryPoints.Yesterday,
                    VictoryPointsLastWeek = faction.VictoryPoints.LastWeek,
                    VictoryPointsTotal = faction.VictoryPoints.Total
                });
            }

            foreach (EsiFactionWar war in wars)
            {
                result.FactionWars.Add(new SerializableEveFactionWarsListItem
                {
                    FactionID = war.FactionID,
                    FactionName = GetName(names, war.FactionID),
                    AgainstID = war.AgainstID,
                    AgainstName = GetName(names, war.AgainstID)
                });
            }

            return new EsiQueryResult(result, statsTask.Result.Expires, statsTask.Result.ServerTime);
        }

        private static string GetName(IDictionary<long, string> names, long id)
        {
            string name;
            return names.TryGetValue(id, out name) ? name : String.Empty;
        }

        #region ESI models

        private sealed class EsiFactionStats
        {
            [JsonProperty("faction_id")] public int FactionID { get; set; }
            [JsonProperty("pilots")] public int Pilots { get; set; }
            [JsonProperty("systems_controlled")] public int SystemsControlled { get; set; }
            [JsonProperty("kills")] public EsiTotals Kills { get; set; } = new EsiTotals();
            [JsonProperty("victory_points")] public EsiTotals VictoryPoints { get; set; } = new EsiTotals();
        }

        private sealed class EsiTotals
        {
            [JsonProperty("yesterday")] public int Yesterday { get; set; }
            [JsonProperty("last_week")] public int LastWeek { get; set; }
            [JsonProperty("total")] public int Total { get; set; }
        }

        private sealed class EsiFactionWar
        {
            [JsonProperty("faction_id")] public int FactionID { get; set; }
            [JsonProperty("against_id")] public int AgainstID { get; set; }
        }

        #endregion
    }
}
