using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EVEMon.Common.Data;
using EVEMon.Common.Net;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Resolves the names of IDs returned by ESI, with an in-memory cache.
    /// </summary>
    internal static class EsiNameResolver
    {
        private const int MaxIDsPerRequest = 1000;

        // Upwell structure IDs are above this; NPC stations, systems, corporations etc. are below
        private const long MinStructureID = 1000000000000;

        private static readonly ConcurrentDictionary<long, string> s_names = new ConcurrentDictionary<long, string>();
        private static readonly ConcurrentDictionary<long, int> s_structureSystems = new ConcurrentDictionary<long, int>();
        private static Dictionary<int, string> s_races;
        private static Dictionary<int, string> s_bloodlines;
        private static Dictionary<int, string> s_ancestries;

        /// <summary>
        /// Resolves the names of characters, corporations, alliances, factions, NPC stations, systems...
        /// Unknown IDs are left out of the result.
        /// </summary>
        /// <param name="ids">The IDs.</param>
        /// <returns>The names by ID.</returns>
        internal static async Task<IDictionary<long, string>> GetNamesAsync(IEnumerable<long> ids)
        {
            List<long> distinct = ids.Where(id => id > 0 && id < MinStructureID).Distinct().ToList();
            List<long> missing = distinct.Where(id => !s_names.ContainsKey(id)).ToList();

            for (int index = 0; index < missing.Count; index += MaxIDsPerRequest)
            {
                await QueryNamesAsync(missing.Skip(index).Take(MaxIDsPerRequest).ToList()).ConfigureAwait(false);
            }

            string name;
            return distinct.Where(id => s_names.TryGetValue(id, out name))
                .ToDictionary(id => id, id => s_names[id]);
        }

        /// <summary>
        /// Gets the name of a single ID, or null.
        /// </summary>
        internal static async Task<string> GetNameAsync(long id)
        {
            if (id <= 0)
                return null;

            IDictionary<long, string> names = await GetNamesAsync(new[] { id }).ConfigureAwait(false);
            string name;
            return names.TryGetValue(id, out name) ? name : null;
        }

        /// <summary>
        /// Gets the name of a location (NPC station, Upwell structure or solar system).
        /// </summary>
        /// <param name="locationID">The location ID.</param>
        /// <param name="accessToken">An access token with the structures scope, used for structures.</param>
        /// <returns>The name, or a placeholder when it can't be resolved.</returns>
        internal static async Task<string> GetLocationNameAsync(long locationID, string accessToken)
        {
            if (locationID <= 0)
                return String.Empty;

            Station station = locationID <= Int32.MaxValue ? StaticGeography.GetStationByID(locationID) : null;
            if (station != null)
                return station.Name;

            if (locationID < MinStructureID)
            {
                SolarSystem system = locationID <= Int32.MaxValue
                    ? StaticGeography.GetSolarSystemByID((int)locationID)
                    : null;
                return system?.Name ?? await GetNameAsync(locationID).ConfigureAwait(false) ?? $"Location {locationID}";
            }

            string name;
            if (s_names.TryGetValue(locationID, out name))
                return name;

            // Structures can only be resolved if the character has docking access
            try
            {
                EsiResponse<EsiStructure> structure = await EsiClient
                    .GetAsync<EsiStructure>($"/universe/structures/{locationID}/", accessToken).ConfigureAwait(false);
                name = structure.Result?.Name;
                if (structure.Result != null)
                    s_structureSystems[locationID] = structure.Result.SolarSystemID;
            }
            catch (HttpWebClientServiceException)
            {
                name = null;
            }

            name = String.IsNullOrEmpty(name) ? $"Structure {locationID}" : name;
            s_names[locationID] = name;
            return name;
        }

        /// <summary>
        /// Gets the name of a structure already resolved by an ESI query, without querying.
        /// </summary>
        /// <param name="structureID">The structure ID.</param>
        /// <returns>The name, or null.</returns>
        internal static string GetCachedStructureName(long structureID)
        {
            string name;
            return structureID >= MinStructureID && s_names.TryGetValue(structureID, out name) ? name : null;
        }

        /// <summary>
        /// Gets the solar system of a location (NPC station, Upwell structure or solar system), or 0 if unknown.
        /// </summary>
        /// <param name="locationID">The location ID.</param>
        /// <param name="accessToken">An access token with the structures scope, used for structures.</param>
        internal static async Task<int> GetLocationSolarSystemIDAsync(long locationID, string accessToken)
        {
            if (locationID <= 0)
                return 0;

            Station station = locationID <= Int32.MaxValue ? StaticGeography.GetStationByID(locationID) : null;
            if (station != null)
                return station.SolarSystem?.ID ?? 0;

            if (locationID < MinStructureID)
                return locationID <= Int32.MaxValue && StaticGeography.GetSolarSystemByID((int)locationID) != null
                    ? (int)locationID
                    : 0;

            int systemID;
            if (s_structureSystems.TryGetValue(locationID, out systemID))
                return systemID;

            await GetLocationNameAsync(locationID, accessToken).ConfigureAwait(false);
            return s_structureSystems.TryGetValue(locationID, out systemID) ? systemID : 0;
        }

        /// <summary>
        /// Gets the names of the race, bloodline and ancestry.
        /// </summary>
        internal static async Task<Tuple<string, string, string>> GetOriginNamesAsync(int raceID, int bloodlineID,
            int? ancestryID)
        {
            if (s_races == null)
            {
                EsiResponse<List<EsiRace>> races = await EsiClient.GetAsync<List<EsiRace>>("/universe/races/").ConfigureAwait(false);
                s_races = races.Result.ToDictionary(x => x.RaceID, x => x.Name);
            }

            if (s_bloodlines == null)
            {
                EsiResponse<List<EsiBloodline>> bloodlines = await EsiClient
                    .GetAsync<List<EsiBloodline>>("/universe/bloodlines/").ConfigureAwait(false);
                s_bloodlines = bloodlines.Result.ToDictionary(x => x.BloodlineID, x => x.Name);
            }

            if (s_ancestries == null)
            {
                EsiResponse<List<EsiAncestry>> ancestries = await EsiClient
                    .GetAsync<List<EsiAncestry>>("/universe/ancestries/").ConfigureAwait(false);
                s_ancestries = ancestries.Result.ToDictionary(x => x.ID, x => x.Name);
            }

            string race, bloodline, ancestry = null;
            s_races.TryGetValue(raceID, out race);
            s_bloodlines.TryGetValue(bloodlineID, out bloodline);
            if (ancestryID.HasValue)
                s_ancestries.TryGetValue(ancestryID.Value, out ancestry);

            return Tuple.Create(race ?? String.Empty, bloodline ?? String.Empty, ancestry ?? String.Empty);
        }

        /// <summary>
        /// Queries /universe/names/; ESI rejects the whole batch if one ID is invalid, so split it on failure.
        /// </summary>
        private static async Task QueryNamesAsync(IList<long> ids)
        {
            if (!ids.Any())
                return;

            try
            {
                EsiResponse<List<EsiName>> response = await EsiClient
                    .PostAsync<List<EsiName>>("/universe/names/", ids).ConfigureAwait(false);

                foreach (EsiName name in response.Result)
                {
                    s_names[name.ID] = name.Name;
                }
            }
            catch (HttpWebClientServiceException ex) when (ex.ResponseStatusCode == HttpStatusCode.NotFound ||
                                                           ex.ResponseStatusCode == HttpStatusCode.BadRequest)
            {
                if (ids.Count == 1)
                    return;

                int half = ids.Count / 2;
                await QueryNamesAsync(ids.Take(half).ToList()).ConfigureAwait(false);
                await QueryNamesAsync(ids.Skip(half).ToList()).ConfigureAwait(false);
            }
        }
    }
}
