using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EVEMon.Common.Enumerations.CCPAPI;
using EVEMon.Common.Serialization.Eve;
using Newtonsoft.Json;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Wallet journal and transactions, market orders and industry jobs.
    /// </summary>
    internal static partial class EsiCharacterQueries
    {
        // XML API values the model still uses
        private const int BountyPrizesRefTypeID = 85;
        private const int PersonalWalletDivision = 1000;
        private const int StationRange = -1;
        private const int SolarSystemRange = 0;
        private const int RegionRange = 32767;

        /// <summary>
        /// Wallet journal (the last 30 days).
        /// </summary>
        internal static async Task<EsiQueryResult> WalletJournalAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiJournalEntry>> response = await EsiClient
                .GetAllPagesAsync<EsiJournalEntry>($"/characters/{context.CharacterID}/wallet/journal/", context.AccessToken)
                .ConfigureAwait(false);

            IDictionary<long, string> names = await EsiNameResolver
                .GetNamesAsync(response.Result.SelectMany(x => new[] { x.FirstPartyID ?? 0, x.SecondPartyID ?? 0 }))
                .ConfigureAwait(false);

            SerializableAPIWalletJournal journal = new SerializableAPIWalletJournal();
            foreach (EsiJournalEntry entry in response.Result)
            {
                journal.WalletJournal.Add(new SerializableWalletJournalListItem
                {
                    ID = entry.ID,
                    RefTypeID = entry.RefType == "bounty_prizes" ? BountyPrizesRefTypeID : 0,
                    RefTypeName = GetRefTypeName(entry.RefType),
                    OwnerID1 = entry.FirstPartyID ?? 0,
                    OwnerName1 = GetName(names, entry.FirstPartyID ?? 0),
                    OwnerID2 = entry.SecondPartyID ?? 0,
                    OwnerName2 = GetName(names, entry.SecondPartyID ?? 0),
                    ArgID1 = entry.ContextID ?? 0,
                    ArgName1 = String.Empty,
                    Amount = entry.Amount ?? 0,
                    Balance = entry.Balance ?? 0,
                    Reason = entry.Reason ?? String.Empty,
                    TaxReceiverID = entry.TaxReceiverID ?? 0,
                    TaxAmount = entry.Tax ?? 0,
                    Date = entry.Date
                });
            }

            return new EsiQueryResult(journal, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Wallet transactions (the last 2500).
        /// </summary>
        internal static async Task<EsiQueryResult> WalletTransactionsAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiTransaction>> response = await EsiClient
                .GetAsync<List<EsiTransaction>>($"/characters/{context.CharacterID}/wallet/transactions/", context.AccessToken)
                .ConfigureAwait(false);

            IDictionary<long, string> names = await EsiNameResolver
                .GetNamesAsync(response.Result.Select(x => x.ClientID)).ConfigureAwait(false);
            Dictionary<long, string> locations = await GetLocationNamesAsync(response.Result.Select(x => x.LocationID),
                context.AccessToken).ConfigureAwait(false);

            SerializableAPIWalletTransactions transactions = new SerializableAPIWalletTransactions();
            foreach (EsiTransaction transaction in response.Result)
            {
                transactions.WalletTransactions.Add(new SerializableWalletTransactionsListItem
                {
                    ID = transaction.TransactionID,
                    JournalTransactionID = transaction.JournalRefID,
                    Quantity = transaction.Quantity,
                    TypeID = transaction.TypeID,
                    TypeName = GetItemName(transaction.TypeID),
                    Price = transaction.UnitPrice,
                    ClientID = transaction.ClientID,
                    ClientName = GetName(names, transaction.ClientID),
                    StationID = transaction.LocationID,
                    StationName = locations[transaction.LocationID],
                    TransactionType = transaction.IsBuy ? "buy" : "sell",
                    TransactionFor = transaction.IsPersonal ? "personal" : "corporation",
                    TransactionDate = transaction.Date
                });
            }

            return new EsiQueryResult(transactions, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Open market orders, and the closed ones of the last 90 days.
        /// </summary>
        internal static Task<EsiQueryResult> MarketOrdersAsync(EsiQueryContext context)
            => BuildMarketOrdersAsync(context, $"/characters/{context.CharacterID}", false);

        /// <summary>
        /// Open market orders, and the closed ones of the last 90 days, of a character or a corporation.
        /// </summary>
        private static async Task<EsiQueryResult> BuildMarketOrdersAsync(EsiQueryContext context, string ownerPath,
            bool isCorporation)
        {
            Task<EsiResponse<List<EsiMarketOrder>>> openTask = isCorporation
                ? EsiClient.GetAllPagesAsync<EsiMarketOrder>($"{ownerPath}/orders/", context.AccessToken)
                : EsiClient.GetAsync<List<EsiMarketOrder>>($"{ownerPath}/orders/", context.AccessToken);
            Task<EsiResponse<List<EsiMarketOrder>>> historyTask = EsiClient
                .GetAllPagesAsync<EsiMarketOrder>($"{ownerPath}/orders/history/", context.AccessToken);

            await Task.WhenAll(openTask, historyTask).ConfigureAwait(false);

            SerializableAPIMarketOrders orders = new SerializableAPIMarketOrders();
            foreach (EsiMarketOrder order in openTask.Result.Result.Concat(historyTask.Result.Result))
            {
                orders.Orders.Add(new SerializableOrderListItem
                {
                    OrderID = order.OrderID,
                    OwnerID = order.IssuedBy ?? context.CharacterID,
                    ItemID = order.TypeID,
                    StationID = order.LocationID,
                    InitialVolume = order.VolumeTotal,
                    RemainingVolume = order.VolumeRemain,
                    MinVolume = order.MinVolume ?? 1,
                    State = (int)GetOrderState(order.State),
                    Range = GetOrderRange(order.Range),
                    // The XML API used 1000 for the personal wallet and 1000-1006 for the corporation divisions
                    DivisionKey = order.WalletDivision.HasValue ? PersonalWalletDivision + order.WalletDivision.Value - 1 : PersonalWalletDivision,
                    Duration = order.Duration,
                    Escrow = order.Escrow ?? 0,
                    UnitaryPrice = order.Price,
                    IsBuyOrder = order.IsBuyOrder == true ? 1 : 0,
                    Issued = order.Issued
                });
            }

            return new EsiQueryResult(orders, openTask.Result.Expires, openTask.Result.ServerTime);
        }

        /// <summary>
        /// Industry jobs, including the completed ones.
        /// </summary>
        internal static Task<EsiQueryResult> IndustryJobsAsync(EsiQueryContext context)
            => BuildIndustryJobsAsync(context, $"/characters/{context.CharacterID}");

        /// <summary>
        /// Industry jobs of a character or a corporation, including the completed ones.
        /// </summary>
        private static async Task<EsiQueryResult> BuildIndustryJobsAsync(EsiQueryContext context, string ownerPath)
        {
            EsiResponse<List<EsiIndustryJob>> response = await EsiClient
                .GetAllPagesAsync<EsiIndustryJob>($"{ownerPath}/industry/jobs/?include_completed=true", context.AccessToken)
                .ConfigureAwait(false);

            IDictionary<long, string> names = await EsiNameResolver
                .GetNamesAsync(response.Result.Select(x => x.InstallerID)).ConfigureAwait(false);

            SerializableAPIIndustryJobs jobs = new SerializableAPIIndustryJobs();
            foreach (EsiIndustryJob job in response.Result)
            {
                int solarSystemID = await EsiNameResolver.GetLocationSolarSystemIDAsync(job.StationID, context.AccessToken)
                    .ConfigureAwait(false);

                jobs.Jobs.Add(new SerializableJobListItem
                {
                    JobID = job.JobID,
                    InstallerID = job.InstallerID,
                    InstallerName = GetName(names, job.InstallerID),
                    FacilityID = job.FacilityID,
                    SolarSystemID = solarSystemID,
                    SolarSystemName = SolarSystemName(solarSystemID),
                    StationID = job.StationID,
                    ActivityID = job.ActivityID,
                    BlueprintID = job.BlueprintID,
                    BlueprintTypeID = job.BlueprintTypeID,
                    BlueprintTypeName = GetItemName(job.BlueprintTypeID),
                    BlueprintLocationID = job.BlueprintLocationID,
                    OutputLocationID = job.OutputLocationID,
                    Runs = job.Runs,
                    Cost = job.Cost ?? 0,
                    LicensedRuns = job.LicensedRuns ?? 0,
                    Probability = job.Probability ?? 0,
                    ProductTypeID = job.ProductTypeID ?? 0,
                    ProductTypeName = job.ProductTypeID.HasValue ? GetItemName(job.ProductTypeID.Value) : String.Empty,
                    Status = (int)GetJobStatus(job.Status),
                    TimeInSeconds = job.Duration,
                    CompletedCharacterID = job.CompletedCharacterID ?? 0,
                    SuccessfulRuns = job.SuccessfulRuns ?? 0,
                    StartDate = job.StartDate,
                    EndDate = job.EndDate,
                    PauseDate = job.PauseDate ?? DateTime.MinValue,
                    CompletedDate = job.CompletedDate ?? DateTime.MinValue
                });
            }

            return new EsiQueryResult(jobs, response.Expires, response.ServerTime);
        }

        #region Helpers

        /// <summary>
        /// Turns an ESI journal reference type ("player_donation") into a readable name ("Player Donation").
        /// </summary>
        private static string GetRefTypeName(string refType)
            => String.IsNullOrEmpty(refType)
                ? String.Empty
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(refType.Replace('_', ' '));

        private static CCPOrderState GetOrderState(string state)
        {
            switch (state)
            {
                case "cancelled":
                    return CCPOrderState.Canceled;
                case "expired":
                    return CCPOrderState.ExpiredOrFulfilled;
                default:
                    // Open orders have no state
                    return CCPOrderState.Opened;
            }
        }

        private static int GetOrderRange(string range)
        {
            switch (range)
            {
                case "station":
                    return StationRange;
                case "solarsystem":
                    return SolarSystemRange;
                case "region":
                    return RegionRange;
                default:
                    int jumps;
                    return Int32.TryParse(range, NumberStyles.Integer, CultureInfo.InvariantCulture, out jumps)
                        ? jumps
                        : StationRange;
            }
        }

        private static CCPJobCompletedStatus GetJobStatus(string status)
        {
            switch (status)
            {
                case "paused":
                    return CCPJobCompletedStatus.Paused;
                case "ready":
                    return CCPJobCompletedStatus.Ready;
                case "delivered":
                    return CCPJobCompletedStatus.Delivered;
                case "cancelled":
                    return CCPJobCompletedStatus.Canceled;
                case "reverted":
                    return CCPJobCompletedStatus.Reverted;
                default:
                    return CCPJobCompletedStatus.Installed;
            }
        }

        private static string SolarSystemName(int solarSystemID)
            => solarSystemID > 0 ? Data.StaticGeography.GetSolarSystemByID(solarSystemID)?.Name ?? String.Empty : String.Empty;

        /// <summary>
        /// Gets the names of the given locations (stations, structures, systems).
        /// </summary>
        private static async Task<Dictionary<long, string>> GetLocationNamesAsync(IEnumerable<long> locationIDs,
            string accessToken)
        {
            Dictionary<long, string> names = new Dictionary<long, string>();
            foreach (long locationID in locationIDs.Distinct())
            {
                names[locationID] = await EsiNameResolver.GetLocationNameAsync(locationID, accessToken).ConfigureAwait(false);
            }
            return names;
        }

        #endregion

        #region ESI models

        private sealed class EsiJournalEntry
        {
            [JsonProperty("id")] public long ID { get; set; }
            [JsonProperty("date")] public DateTime Date { get; set; }
            [JsonProperty("ref_type")] public string RefType { get; set; }
            [JsonProperty("first_party_id")] public long? FirstPartyID { get; set; }
            [JsonProperty("second_party_id")] public long? SecondPartyID { get; set; }
            [JsonProperty("context_id")] public long? ContextID { get; set; }
            [JsonProperty("amount")] public decimal? Amount { get; set; }
            [JsonProperty("balance")] public decimal? Balance { get; set; }
            [JsonProperty("reason")] public string Reason { get; set; }
            [JsonProperty("tax")] public decimal? Tax { get; set; }
            [JsonProperty("tax_receiver_id")] public long? TaxReceiverID { get; set; }
        }

        private sealed class EsiTransaction
        {
            [JsonProperty("transaction_id")] public long TransactionID { get; set; }
            [JsonProperty("journal_ref_id")] public long JournalRefID { get; set; }
            [JsonProperty("date")] public DateTime Date { get; set; }
            [JsonProperty("type_id")] public int TypeID { get; set; }
            [JsonProperty("quantity")] public long Quantity { get; set; }
            [JsonProperty("unit_price")] public decimal UnitPrice { get; set; }
            [JsonProperty("client_id")] public long ClientID { get; set; }
            [JsonProperty("location_id")] public long LocationID { get; set; }
            [JsonProperty("is_buy")] public bool IsBuy { get; set; }
            [JsonProperty("is_personal")] public bool IsPersonal { get; set; }
        }

        private sealed class EsiMarketOrder
        {
            [JsonProperty("order_id")] public long OrderID { get; set; }
            [JsonProperty("type_id")] public int TypeID { get; set; }
            [JsonProperty("location_id")] public long LocationID { get; set; }
            [JsonProperty("volume_total")] public int VolumeTotal { get; set; }
            [JsonProperty("volume_remain")] public int VolumeRemain { get; set; }
            [JsonProperty("min_volume")] public int? MinVolume { get; set; }
            [JsonProperty("price")] public decimal Price { get; set; }
            [JsonProperty("escrow")] public decimal? Escrow { get; set; }
            [JsonProperty("is_buy_order")] public bool? IsBuyOrder { get; set; }
            [JsonProperty("range")] public string Range { get; set; }
            [JsonProperty("duration")] public int Duration { get; set; }
            [JsonProperty("issued")] public DateTime Issued { get; set; }
            [JsonProperty("state")] public string State { get; set; }
            [JsonProperty("issued_by")] public long? IssuedBy { get; set; }
            [JsonProperty("wallet_division")] public int? WalletDivision { get; set; }
        }

        private sealed class EsiIndustryJob
        {
            [JsonProperty("job_id")] public long JobID { get; set; }
            [JsonProperty("installer_id")] public long InstallerID { get; set; }
            [JsonProperty("facility_id")] public long FacilityID { get; set; }
            [JsonProperty("station_id")] public long StationID { get; set; }
            [JsonProperty("activity_id")] public int ActivityID { get; set; }
            [JsonProperty("blueprint_id")] public long BlueprintID { get; set; }
            [JsonProperty("blueprint_type_id")] public int BlueprintTypeID { get; set; }
            [JsonProperty("blueprint_location_id")] public long BlueprintLocationID { get; set; }
            [JsonProperty("output_location_id")] public long OutputLocationID { get; set; }
            [JsonProperty("runs")] public int Runs { get; set; }
            [JsonProperty("cost")] public double? Cost { get; set; }
            [JsonProperty("licensed_runs")] public int? LicensedRuns { get; set; }
            [JsonProperty("probability")] public double? Probability { get; set; }
            [JsonProperty("product_type_id")] public int? ProductTypeID { get; set; }
            [JsonProperty("status")] public string Status { get; set; }
            [JsonProperty("duration")] public int Duration { get; set; }
            [JsonProperty("start_date")] public DateTime StartDate { get; set; }
            [JsonProperty("end_date")] public DateTime EndDate { get; set; }
            [JsonProperty("pause_date")] public DateTime? PauseDate { get; set; }
            [JsonProperty("completed_date")] public DateTime? CompletedDate { get; set; }
            [JsonProperty("completed_character_id")] public long? CompletedCharacterID { get; set; }
            [JsonProperty("successful_runs")] public int? SuccessfulRuns { get; set; }
        }

        #endregion
    }
}
