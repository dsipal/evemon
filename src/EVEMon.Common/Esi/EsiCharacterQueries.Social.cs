using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EVEMon.Common.Constants;
using EVEMon.Common.Net;
using EVEMon.Common.Serialization.Eve;
using Newtonsoft.Json;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Standings, contacts, medals, factional warfare and research.
    /// </summary>
    internal static partial class EsiCharacterQueries
    {
        // XML API type IDs used to tell contacts apart (see Contact)
        private const long CharacterContactTypeID = 1373;
        private const long FactionContactTypeID = 30;

        // XML API error code for "character not enlisted in factional warfare"
        private const int NotEnlistedErrorCode = 124;

        /// <summary>
        /// NPC standings (agents, NPC corporations and factions).
        /// </summary>
        internal static async Task<EsiQueryResult> StandingsAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiStanding>> response = await EsiClient
                .GetAsync<List<EsiStanding>>($"/characters/{context.CharacterID}/standings/", context.AccessToken)
                .ConfigureAwait(false);

            IDictionary<long, string> names = await EsiNameResolver
                .GetNamesAsync(response.Result.Select(x => x.FromID)).ConfigureAwait(false);

            SerializableStandings standings = new SerializableStandings();
            foreach (EsiStanding standing in response.Result)
            {
                SerializableStandingsListItem item = new SerializableStandingsListItem
                {
                    ID = (int)standing.FromID,
                    Name = GetName(names, standing.FromID),
                    StandingValue = standing.Standing
                };

                switch (standing.FromType)
                {
                    case "agent":
                        standings.AgentStandings.Add(item);
                        break;
                    case "npc_corp":
                        standings.NPCCorporationStandings.Add(item);
                        break;
                    case "faction":
                        standings.FactionStandings.Add(item);
                        break;
                }
            }

            return new EsiQueryResult(new SerializableAPIStandings { CharacterNPCStandings = standings },
                response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Personal, corporation and alliance contacts.
        /// </summary>
        internal static async Task<EsiQueryResult> ContactsAsync(EsiQueryContext context)
        {
            EsiResponse<EsiCharacterPublicInfo> info = await EsiClient
                .GetAsync<EsiCharacterPublicInfo>($"/characters/{context.CharacterID}/").ConfigureAwait(false);

            Task<EsiResponse<List<EsiContact>>> personalTask =
                EsiClient.GetAllPagesAsync<EsiContact>($"/characters/{context.CharacterID}/contacts/", context.AccessToken);
            Task<EsiResponse<List<EsiContact>>> corporationTask =
                GetOptionalContactsAsync($"/corporations/{info.Result.CorporationID}/contacts/", context.AccessToken);
            Task<EsiResponse<List<EsiContact>>> allianceTask = info.Result.AllianceID.HasValue
                ? GetOptionalContactsAsync($"/alliances/{info.Result.AllianceID}/contacts/", context.AccessToken)
                : Task.FromResult<EsiResponse<List<EsiContact>>>(null);

            await Task.WhenAll(personalTask, corporationTask, allianceTask).ConfigureAwait(false);

            List<EsiContact> all = personalTask.Result.Result
                .Concat(corporationTask.Result?.Result ?? Enumerable.Empty<EsiContact>())
                .Concat(allianceTask.Result?.Result ?? Enumerable.Empty<EsiContact>())
                .ToList();
            IDictionary<long, string> names = await EsiNameResolver.GetNamesAsync(all.Select(x => x.ContactID))
                .ConfigureAwait(false);

            SerializableAPIContactList contacts = new SerializableAPIContactList();
            AddContacts(contacts.Contacts, personalTask.Result.Result, names);
            AddContacts(contacts.CorporateContacts, corporationTask.Result?.Result, names);
            AddContacts(contacts.AllianceContacts, allianceTask.Result?.Result, names);

            return new EsiQueryResult(contacts, personalTask.Result.Expires, personalTask.Result.ServerTime);
        }

        /// <summary>
        /// Medals awarded to the character.
        /// </summary>
        internal static async Task<EsiQueryResult> MedalsAsync(EsiQueryContext context)
        {
            Task<EsiResponse<EsiCharacterPublicInfo>> infoTask =
                EsiClient.GetAsync<EsiCharacterPublicInfo>($"/characters/{context.CharacterID}/");
            Task<EsiResponse<List<EsiMedal>>> medalsTask =
                EsiClient.GetAsync<List<EsiMedal>>($"/characters/{context.CharacterID}/medals/", context.AccessToken);

            await Task.WhenAll(infoTask, medalsTask).ConfigureAwait(false);

            SerializableAPIMedals medals = new SerializableAPIMedals();
            foreach (EsiMedal medal in medalsTask.Result.Result)
            {
                SerializableMedalsListItem item = new SerializableMedalsListItem
                {
                    MedalID = medal.MedalID,
                    Reason = medal.Reason ?? String.Empty,
                    Status = medal.Status,
                    IssuerID = medal.IssuerID,
                    Issued = medal.Date,
                    CorporationID = medal.CorporationID,
                    Title = medal.Title ?? String.Empty,
                    Description = medal.Description ?? String.Empty
                };

                if (medal.CorporationID == infoTask.Result.Result.CorporationID)
                    medals.CurrentCorpMedals.Add(item);
                else
                    medals.OtherCorpsMedals.Add(item);
            }

            return new EsiQueryResult(medals, medalsTask.Result.Expires, medalsTask.Result.ServerTime);
        }

        /// <summary>
        /// Factional warfare statistics.
        /// </summary>
        internal static async Task<EsiQueryResult> FactionalWarfareStatsAsync(EsiQueryContext context)
        {
            EsiResponse<EsiFactionalWarfareStats> response = await EsiClient
                .GetAsync<EsiFactionalWarfareStats>($"/characters/{context.CharacterID}/fw/stats/", context.AccessToken)
                .ConfigureAwait(false);

            EsiFactionalWarfareStats stats = response.Result;

            // The XML API reported "not enlisted" as an error, which the model expects
            if (!stats.FactionID.HasValue)
            {
                return new EsiQueryResult(null, response.Expires, response.ServerTime, new CCPAPIError
                {
                    ErrorCode = NotEnlistedErrorCode,
                    ErrorMessage = "Character is not enlisted in Factional Warfare."
                });
            }

            string factionName = await EsiNameResolver.GetNameAsync(stats.FactionID.Value).ConfigureAwait(false);

            SerializableAPIFactionalWarfareStats result = new SerializableAPIFactionalWarfareStats
            {
                FactionID = stats.FactionID.Value,
                FactionName = factionName ?? String.Empty,
                EnlistedDate = stats.EnlistedOn ?? DateTime.MinValue,
                CurrentRank = stats.CurrentRank ?? 0,
                HighestRank = stats.HighestRank ?? 0,
                KillsYesterday = stats.Kills?.Yesterday ?? 0,
                KillsLastWeek = stats.Kills?.LastWeek ?? 0,
                KillsTotal = stats.Kills?.Total ?? 0,
                VictoryPointsYesterday = stats.VictoryPoints?.Yesterday ?? 0,
                VictoryPointsLastWeek = stats.VictoryPoints?.LastWeek ?? 0,
                VictoryPointsTotal = stats.VictoryPoints?.Total ?? 0
            };

            return new EsiQueryResult(result, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Research agents.
        /// </summary>
        internal static async Task<EsiQueryResult> ResearchPointsAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiResearchAgent>> response = await EsiClient
                .GetAsync<List<EsiResearchAgent>>($"/characters/{context.CharacterID}/agents_research/", context.AccessToken)
                .ConfigureAwait(false);

            SerializableAPIResearch research = new SerializableAPIResearch();
            foreach (EsiResearchAgent agent in response.Result)
            {
                research.ResearchPoints.Add(new SerializableResearchListItem
                {
                    AgentID = agent.AgentID,
                    SkillID = agent.SkillTypeID,
                    ResearchStartDate = agent.StartedAt,
                    PointsPerDay = agent.PointsPerDay,
                    RemainderPoints = agent.RemainderPoints
                });
            }

            return new EsiQueryResult(research, response.Expires, response.ServerTime);
        }

        #region Helpers

        /// <summary>
        /// Gets corporation or alliance contacts, which are optional (e.g. NPC corporations have none).
        /// </summary>
        private static async Task<EsiResponse<List<EsiContact>>> GetOptionalContactsAsync(string path, string accessToken)
        {
            try
            {
                return await EsiClient.GetAllPagesAsync<EsiContact>(path, accessToken).ConfigureAwait(false);
            }
            catch (HttpWebClientServiceException ex) when (ex.ResponseStatusCode == HttpStatusCode.Forbidden ||
                                                           ex.ResponseStatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        private static void AddContacts(ICollection<SerializableContactListItem> target, IEnumerable<EsiContact> contacts,
            IDictionary<long, string> names)
        {
            if (contacts == null)
                return;

            foreach (EsiContact contact in contacts)
            {
                target.Add(new SerializableContactListItem
                {
                    ContactID = contact.ContactID,
                    ContactName = GetName(names, contact.ContactID),
                    Standing = contact.Standing,
                    InWatchlist = contact.IsWatched ?? false,
                    ContactTypeID = GetContactTypeID(contact.ContactType)
                });
            }
        }

        private static long GetContactTypeID(string contactType)
        {
            switch (contactType)
            {
                case "corporation":
                    return DBConstants.CorporationID;
                case "alliance":
                    return DBConstants.AllianceID;
                case "faction":
                    return FactionContactTypeID;
                default:
                    return CharacterContactTypeID;
            }
        }

        #endregion

        #region ESI models

        private sealed class EsiStanding
        {
            [JsonProperty("from_id")] public long FromID { get; set; }
            [JsonProperty("from_type")] public string FromType { get; set; }
            [JsonProperty("standing")] public double Standing { get; set; }
        }

        private sealed class EsiContact
        {
            [JsonProperty("contact_id")] public long ContactID { get; set; }
            [JsonProperty("contact_type")] public string ContactType { get; set; }
            [JsonProperty("standing")] public float Standing { get; set; }
            [JsonProperty("is_watched")] public bool? IsWatched { get; set; }
        }

        private sealed class EsiMedal
        {
            [JsonProperty("medal_id")] public long MedalID { get; set; }
            [JsonProperty("corporation_id")] public long CorporationID { get; set; }
            [JsonProperty("issuer_id")] public long IssuerID { get; set; }
            [JsonProperty("date")] public DateTime Date { get; set; }
            [JsonProperty("title")] public string Title { get; set; }
            [JsonProperty("description")] public string Description { get; set; }
            [JsonProperty("reason")] public string Reason { get; set; }
            [JsonProperty("status")] public string Status { get; set; }
        }

        private sealed class EsiFactionalWarfareStats
        {
            [JsonProperty("faction_id")] public int? FactionID { get; set; }
            [JsonProperty("enlisted_on")] public DateTime? EnlistedOn { get; set; }
            [JsonProperty("current_rank")] public int? CurrentRank { get; set; }
            [JsonProperty("highest_rank")] public int? HighestRank { get; set; }
            [JsonProperty("kills")] public EsiFactionalWarfareTotals Kills { get; set; }
            [JsonProperty("victory_points")] public EsiFactionalWarfareTotals VictoryPoints { get; set; }
        }

        private sealed class EsiFactionalWarfareTotals
        {
            [JsonProperty("yesterday")] public int Yesterday { get; set; }
            [JsonProperty("last_week")] public int LastWeek { get; set; }
            [JsonProperty("total")] public int Total { get; set; }
        }

        private sealed class EsiResearchAgent
        {
            [JsonProperty("agent_id")] public int AgentID { get; set; }
            [JsonProperty("skill_type_id")] public int SkillTypeID { get; set; }
            [JsonProperty("started_at")] public DateTime StartedAt { get; set; }
            [JsonProperty("points_per_day")] public double PointsPerDay { get; set; }
            [JsonProperty("remainder_points")] public float RemainderPoints { get; set; }
        }

        #endregion
    }
}
