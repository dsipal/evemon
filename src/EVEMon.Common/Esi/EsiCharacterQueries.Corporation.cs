using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using EVEMon.Common.Net;
using EVEMon.Common.Serialization.Eve;
using Newtonsoft.Json;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Data of the character's corporation. ESI serves it with the character's token, as long as the character
    /// has the in-game roles each route needs.
    /// </summary>
    internal static partial class EsiCharacterQueries
    {
        // XML API error code for "the character lacks the corporation roles", which the model does not report
        private const int MissingCorporationRolesErrorCode = 206;

        /// <summary>
        /// Medals created by the corporation.
        /// </summary>
        internal static Task<EsiQueryResult> CorporationMedalsAsync(EsiQueryContext context)
            => QueryCorporationAsync(context, async corporationPath =>
            {
                EsiResponse<List<EsiCorporationMedal>> response = await EsiClient
                    .GetAllPagesAsync<EsiCorporationMedal>($"{corporationPath}/medals/", context.AccessToken)
                    .ConfigureAwait(false);

                SerializableAPIMedals medals = new SerializableAPIMedals();
                foreach (EsiCorporationMedal medal in response.Result)
                {
                    medals.CorporationMedals.Add(new SerializableMedalsListItem
                    {
                        MedalID = medal.MedalID,
                        IssuerID = medal.CreatorID,
                        Issued = medal.CreatedAt,
                        Title = medal.Title ?? String.Empty,
                        Description = medal.Description ?? String.Empty,
                        Reason = String.Empty
                    });
                }

                return new EsiQueryResult(medals, response.Expires, response.ServerTime);
            });

        /// <summary>
        /// Market orders of the corporation.
        /// </summary>
        internal static Task<EsiQueryResult> CorporationMarketOrdersAsync(EsiQueryContext context)
            => QueryCorporationAsync(context, corporationPath => BuildMarketOrdersAsync(context, corporationPath, true));

        /// <summary>
        /// Contracts of the corporation.
        /// </summary>
        internal static Task<EsiQueryResult> CorporationContractsAsync(EsiQueryContext context)
            => QueryCorporationAsync(context, corporationPath => BuildContractsAsync(context, corporationPath));

        /// <summary>
        /// Bids on the corporation's auction contracts.
        /// </summary>
        internal static Task<EsiQueryResult> CorporationContractBidsAsync(EsiQueryContext context)
            => QueryCorporationAsync(context, corporationPath => BuildContractBidsAsync(context, corporationPath));

        /// <summary>
        /// Items of a corporation contract (<see cref="EsiQueryContext.ID"/> is the contract ID).
        /// </summary>
        internal static Task<EsiQueryResult> CorporationContractItemsAsync(EsiQueryContext context)
            => QueryCorporationAsync(context, corporationPath => BuildContractItemsAsync(context, corporationPath));

        /// <summary>
        /// Industry jobs of the corporation.
        /// </summary>
        internal static Task<EsiQueryResult> CorporationIndustryJobsAsync(EsiQueryContext context)
            => QueryCorporationAsync(context, corporationPath => BuildIndustryJobsAsync(context, corporationPath));

        /// <summary>
        /// Runs a query on the character's corporation; a missing in-game role is reported the way the XML API did.
        /// </summary>
        private static async Task<EsiQueryResult> QueryCorporationAsync(EsiQueryContext context,
            Func<string, Task<EsiQueryResult>> query)
        {
            EsiResponse<EsiCharacterPublicInfo> info = await EsiClient
                .GetAsync<EsiCharacterPublicInfo>($"/characters/{context.CharacterID}/").ConfigureAwait(false);

            try
            {
                return await query($"/corporations/{info.Result.CorporationID}").ConfigureAwait(false);
            }
            catch (HttpWebClientServiceException ex) when (ex.ResponseStatusCode == HttpStatusCode.Forbidden)
            {
                return new EsiQueryResult(null, DateTime.UtcNow.AddHours(1), info.ServerTime, new CCPAPIError
                {
                    ErrorCode = MissingCorporationRolesErrorCode,
                    ErrorMessage = "The character lacks the corporation roles for this data."
                });
            }
        }

        #region ESI models

        private sealed class EsiCorporationMedal
        {
            [JsonProperty("medal_id")] public long MedalID { get; set; }
            [JsonProperty("creator_id")] public long CreatorID { get; set; }
            [JsonProperty("created_at")] public DateTime CreatedAt { get; set; }
            [JsonProperty("title")] public string Title { get; set; }
            [JsonProperty("description")] public string Description { get; set; }
        }

        #endregion
    }
}
