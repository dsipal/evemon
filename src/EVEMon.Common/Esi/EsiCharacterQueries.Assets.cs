using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EVEMon.Common.Net;
using EVEMon.Common.Serialization.Eve;
using EVEMon.Common.Service;
using Newtonsoft.Json;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Contracts, assets, mail and notifications.
    /// </summary>
    internal static partial class EsiCharacterQueries
    {
        // ESI returns mail headers 50 at a time; the XML API returned up to 200
        private const int MaxMailHeaderRequests = 4;

        #region Contracts

        /// <summary>
        /// Contracts issued by or to the character (the last 30 days).
        /// </summary>
        internal static Task<EsiQueryResult> ContractsAsync(EsiQueryContext context)
            => BuildContractsAsync(context, $"/characters/{context.CharacterID}");

        /// <summary>
        /// Contracts of a character or a corporation.
        /// </summary>
        private static async Task<EsiQueryResult> BuildContractsAsync(EsiQueryContext context, string ownerPath)
        {
            EsiResponse<List<EsiContract>> response = await GetContractsAsync(context, ownerPath).ConfigureAwait(false);

            SerializableAPIContracts contracts = new SerializableAPIContracts();
            foreach (EsiContract contract in response.Result)
            {
                contracts.Contracts.Add(new SerializableContractListItem
                {
                    ContractID = contract.ContractID,
                    IssuerID = contract.IssuerID,
                    IssuerCorpID = contract.IssuerCorporationID,
                    AssigneeID = contract.AssigneeID,
                    AcceptorID = contract.AcceptorID,
                    StartStationID = contract.StartLocationID ?? 0,
                    EndStationID = contract.EndLocationID ?? 0,
                    Type = ToPascalCase(contract.Type),
                    Status = GetContractStatus(contract.Status),
                    Title = contract.Title ?? String.Empty,
                    ForCorp = contract.ForCorporation,
                    Availability = contract.Availability == "public" ? "Public" : "Private",
                    DateIssued = contract.DateIssued,
                    DateExpired = contract.DateExpired,
                    DateAccepted = contract.DateAccepted ?? DateTime.MinValue,
                    DateCompleted = contract.DateCompleted ?? DateTime.MinValue,
                    NumDays = contract.DaysToComplete ?? 0,
                    Price = contract.Price ?? 0,
                    Reward = contract.Reward ?? 0,
                    Collateral = contract.Collateral ?? 0,
                    Buyout = contract.Buyout ?? 0,
                    Volume = (decimal)(contract.Volume ?? 0)
                });
            }

            return new EsiQueryResult(contracts, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Bids on the character's auction contracts (ESI only has them per contract).
        /// </summary>
        internal static Task<EsiQueryResult> ContractBidsAsync(EsiQueryContext context)
            => BuildContractBidsAsync(context, $"/characters/{context.CharacterID}");

        /// <summary>
        /// Bids on the auction contracts of a character or a corporation.
        /// </summary>
        private static async Task<EsiQueryResult> BuildContractBidsAsync(EsiQueryContext context, string ownerPath)
        {
            EsiResponse<List<EsiContract>> contracts = await GetContractsAsync(context, ownerPath).ConfigureAwait(false);

            SerializableAPIContractBids bids = new SerializableAPIContractBids();
            DateTime expires = contracts.Expires;

            foreach (EsiContract auction in contracts.Result.Where(x => x.Type == "auction"))
            {
                EsiResponse<List<EsiContractBid>> response = await EsiClient
                    .GetAsync<List<EsiContractBid>>($"{ownerPath}/contracts/{auction.ContractID}/bids/", context.AccessToken)
                    .ConfigureAwait(false);

                foreach (EsiContractBid bid in response.Result)
                {
                    bids.ContractBids.Add(new SerializableContractBidsListItem
                    {
                        ID = bid.BidID,
                        ContractID = auction.ContractID,
                        BidderID = bid.BidderID,
                        Amount = bid.Amount,
                        DateBid = bid.DateBid
                    });
                }
            }

            return new EsiQueryResult(bids, expires, contracts.ServerTime);
        }

        /// <summary>
        /// Items of a contract (<see cref="EsiQueryContext.ID"/> is the contract ID).
        /// </summary>
        internal static Task<EsiQueryResult> ContractItemsAsync(EsiQueryContext context)
            => BuildContractItemsAsync(context, $"/characters/{context.CharacterID}");

        /// <summary>
        /// Items of a contract of a character or a corporation.
        /// </summary>
        private static async Task<EsiQueryResult> BuildContractItemsAsync(EsiQueryContext context, string ownerPath)
        {
            EsiResponse<List<EsiContractItem>> response = await EsiClient
                .GetAsync<List<EsiContractItem>>($"{ownerPath}/contracts/{context.ID}/items/", context.AccessToken)
                .ConfigureAwait(false);

            SerializableAPIContractItems items = new SerializableAPIContractItems();
            foreach (EsiContractItem item in response.Result)
            {
                items.ContractItems.Add(new SerializableContractItemsListItem
                {
                    RecordID = item.RecordID,
                    TypeID = item.TypeID,
                    Quantity = item.Quantity,
                    RawQuantity = item.RawQuantity ?? 0,
                    Singleton = item.IsSingleton,
                    Included = item.IsIncluded
                });
            }

            return new EsiQueryResult(items, response.Expires, response.ServerTime);
        }

        private static Task<EsiResponse<List<EsiContract>>> GetContractsAsync(EsiQueryContext context, string ownerPath)
            => EsiClient.GetAllPagesAsync<EsiContract>($"{ownerPath}/contracts/", context.AccessToken);

        private static string GetContractStatus(string status)
        {
            switch (status)
            {
                case "finished":
                    return "Completed";
                case "finished_issuer":
                    return "CompletedByIssuer";
                case "finished_contractor":
                    return "CompletedByContractor";
                case "cancelled":
                    return "Canceled";
                default:
                    return ToPascalCase(status);
            }
        }

        /// <summary>
        /// Turns an ESI value ("item_exchange") into the XML API one ("ItemExchange").
        /// </summary>
        private static string ToPascalCase(string value)
            => String.IsNullOrEmpty(value)
                ? String.Empty
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ')).Replace(" ", String.Empty);

        #endregion


        #region Assets

        /// <summary>
        /// Assets, with the content of containers and ships under their top-level item.
        /// </summary>
        internal static async Task<EsiQueryResult> AssetsAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiAsset>> response = await EsiClient
                .GetAllPagesAsync<EsiAsset>($"/characters/{context.CharacterID}/assets/", context.AccessToken)
                .ConfigureAwait(false);

            Dictionary<long, EsiAsset> byItemID = response.Result
                .GroupBy(x => x.ItemID).ToDictionary(x => x.Key, x => x.First());

            // Top-level items are those not located inside another item of the character
            List<EsiAsset> topLevel = response.Result.Where(x => !byItemID.ContainsKey(x.LocationID)).ToList();
            ILookup<long, EsiAsset> children = response.Result
                .Where(x => byItemID.ContainsKey(x.LocationID))
                .ToLookup(x => x.LocationID);

            // Resolve the structures names now, so the assets show where they are
            foreach (long structureID in topLevel.Select(x => x.LocationID).Where(x => x > Int32.MaxValue).Distinct())
            {
                await EsiNameResolver.GetLocationNameAsync(structureID, context.AccessToken).ConfigureAwait(false);
            }

            SerializableAPIAssetList assets = new SerializableAPIAssetList();
            foreach (EsiAsset item in topLevel)
            {
                SerializableAssetListItem asset = ToSerializableAsset(item);
                AddContents(asset, item.ItemID, children);
                assets.Assets.Add(asset);
            }

            return new EsiQueryResult(assets, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Adds every item nested in the given item, at any depth, as its contents.
        /// </summary>
        private static void AddContents(SerializableAssetListItem asset, long itemID, ILookup<long, EsiAsset> children)
        {
            foreach (EsiAsset child in children[itemID])
            {
                asset.Contents.Add(ToSerializableAsset(child));
                AddContents(asset, child.ItemID, children);
            }
        }

        private static SerializableAssetListItem ToSerializableAsset(EsiAsset item)
            => new SerializableAssetListItem
            {
                ItemID = item.ItemID,
                LocationID = item.LocationID,
                TypeID = item.TypeID,
                Quantity = item.Quantity,
                EVEFlag = EveFlag.GetFlagID(item.LocationFlag),
                Singleton = (byte)(item.IsSingleton ? 1 : 0),
                // The XML API used -1 for assembled items / original blueprints and -2 for blueprint copies
                RawQuantity = item.IsBlueprintCopy == true ? -2 : item.IsSingleton ? -1 : 0
            };

        #endregion


        #region Mail

        /// <summary>
        /// Mail headers (the latest 200).
        /// </summary>
        internal static async Task<EsiQueryResult> MailMessagesAsync(EsiQueryContext context)
        {
            List<EsiMailHeader> headers = new List<EsiMailHeader>();
            EsiResponse<List<EsiMailHeader>> first = null;
            long? lastMailID = null;

            for (int request = 0; request < MaxMailHeaderRequests; request++)
            {
                string path = $"/characters/{context.CharacterID}/mail/" +
                              (lastMailID.HasValue ? $"?last_mail_id={lastMailID}" : String.Empty);
                EsiResponse<List<EsiMailHeader>> response = await EsiClient
                    .GetAsync<List<EsiMailHeader>>(path, context.AccessToken).ConfigureAwait(false);

                first = first ?? response;
                if (response.Result == null || !response.Result.Any())
                    break;

                headers.AddRange(response.Result);
                lastMailID = response.Result.Min(x => x.MailID);
            }

            IDictionary<long, string> names = await EsiNameResolver.GetNamesAsync(headers.Select(x => x.From))
                .ConfigureAwait(false);

            SerializableAPIMailMessages messages = new SerializableAPIMailMessages();
            foreach (EsiMailHeader header in headers)
            {
                List<EsiMailRecipient> recipients = header.Recipients ?? new List<EsiMailRecipient>();
                SerializableMailMessagesListItem message = new SerializableMailMessagesListItem
                {
                    MessageID = header.MailID,
                    SenderID = header.From,
                    SenderName = GetName(names, header.From),
                    SentDate = header.Timestamp,
                    Title = header.Subject ?? String.Empty,
                    ToCorpOrAllianceID = recipients
                        .Where(x => x.RecipientType == "corporation" || x.RecipientType == "alliance")
                        .Select(x => x.RecipientID.ToString(CultureInfo.InvariantCulture))
                        .FirstOrDefault() ?? String.Empty
                };

                foreach (EsiMailRecipient recipient in recipients.Where(x => x.RecipientType == "character"))
                {
                    message.ToCharacterIDs.Add(recipient.RecipientID.ToString(CultureInfo.InvariantCulture));
                }

                foreach (EsiMailRecipient recipient in recipients.Where(x => x.RecipientType == "mailing_list"))
                {
                    message.ToListID.Add(recipient.RecipientID.ToString(CultureInfo.InvariantCulture));
                }

                messages.Messages.Add(message);
            }

            return new EsiQueryResult(messages, first?.Expires ?? DateTime.UtcNow, first?.ServerTime ?? DateTime.UtcNow);
        }

        /// <summary>
        /// Mailing lists.
        /// </summary>
        internal static async Task<EsiQueryResult> MailingListsAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiMailingList>> response = await EsiClient
                .GetAsync<List<EsiMailingList>>($"/characters/{context.CharacterID}/mail/lists/", context.AccessToken)
                .ConfigureAwait(false);

            SerializableAPIMailingLists lists = new SerializableAPIMailingLists();
            foreach (EsiMailingList list in response.Result)
            {
                lists.MailingLists.Add(new SerializableMailingListsListItem
                {
                    ID = list.MailingListID,
                    DisplayName = list.Name ?? String.Empty
                });
            }

            return new EsiQueryResult(lists, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Body of a mail (<see cref="EsiQueryContext.ID"/> is the mail ID).
        /// </summary>
        internal static async Task<EsiQueryResult> MailBodyAsync(EsiQueryContext context)
        {
            EsiResponse<EsiMail> response = await EsiClient
                .GetAsync<EsiMail>($"/characters/{context.CharacterID}/mail/{context.ID}/", context.AccessToken)
                .ConfigureAwait(false);

            SerializableAPIMailBodies bodies = new SerializableAPIMailBodies();
            bodies.Bodies.Add(new SerializableMailBodiesListItem
            {
                MessageID = context.ID,
                MessageText = response.Result.Body ?? String.Empty
            });

            return new EsiQueryResult(bodies, response.Expires, response.ServerTime);
        }

        #endregion


        #region Notifications

        /// <summary>
        /// Notifications, with their text.
        /// </summary>
        internal static async Task<EsiQueryResult> NotificationsAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiNotification>> response = await GetNotificationsAsync(context).ConfigureAwait(false);

            IDictionary<long, string> names = await EsiNameResolver
                .GetNamesAsync(response.Result.Select(x => x.SenderID)).ConfigureAwait(false);

            SerializableAPINotifications notifications = new SerializableAPINotifications();
            foreach (EsiNotification notification in response.Result)
            {
                notifications.Notifications.Add(new SerializableNotificationsListItem
                {
                    NotificationID = notification.NotificationID,
                    TypeName = notification.Type,
                    Text = notification.Text ?? String.Empty,
                    SenderID = (int)notification.SenderID,
                    SenderName = names.ContainsKey(notification.SenderID)
                        ? names[notification.SenderID]
                        : ToTitleWords(notification.SenderType),
                    SentDate = notification.Timestamp,
                    Read = notification.IsRead ?? false
                });
            }

            return new EsiQueryResult(notifications, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Text of a notification (<see cref="EsiQueryContext.ID"/> is the notification ID); ESI returns it
        /// with the notifications list.
        /// </summary>
        internal static async Task<EsiQueryResult> NotificationTextAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiNotification>> response = await GetNotificationsAsync(context).ConfigureAwait(false);

            SerializableAPINotificationTexts texts = new SerializableAPINotificationTexts();
            EsiNotification notification = response.Result.FirstOrDefault(x => x.NotificationID == context.ID);
            if (notification != null)
            {
                texts.Texts.Add(new SerializableNotificationTextsListItem
                {
                    NotificationID = notification.NotificationID,
                    NotificationText = notification.Text ?? String.Empty
                });
            }

            return new EsiQueryResult(texts, response.Expires, response.ServerTime);
        }

        private static Task<EsiResponse<List<EsiNotification>>> GetNotificationsAsync(EsiQueryContext context)
            => EsiClient.GetAsync<List<EsiNotification>>($"/characters/{context.CharacterID}/notifications/",
                context.AccessToken);

        /// <summary>
        /// Turns an ESI value ("CorpAllBillMsg" or "corporation") into words ("Corp All Bill Msg", "Corporation").
        /// </summary>
        internal static string ToTitleWords(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;

            string spaced = System.Text.RegularExpressions.Regex.Replace(value.Replace('_', ' '),
                "(?<=[a-z0-9])(?=[A-Z])", " ");
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
        }

        #endregion


        #region ESI models

        private sealed class EsiContract
        {
            [JsonProperty("contract_id")] public long ContractID { get; set; }
            [JsonProperty("issuer_id")] public long IssuerID { get; set; }
            [JsonProperty("issuer_corporation_id")] public long IssuerCorporationID { get; set; }
            [JsonProperty("assignee_id")] public long AssigneeID { get; set; }
            [JsonProperty("acceptor_id")] public long AcceptorID { get; set; }
            [JsonProperty("start_location_id")] public long? StartLocationID { get; set; }
            [JsonProperty("end_location_id")] public long? EndLocationID { get; set; }
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("status")] public string Status { get; set; }
            [JsonProperty("title")] public string Title { get; set; }
            [JsonProperty("for_corporation")] public bool ForCorporation { get; set; }
            [JsonProperty("availability")] public string Availability { get; set; }
            [JsonProperty("date_issued")] public DateTime DateIssued { get; set; }
            [JsonProperty("date_expired")] public DateTime DateExpired { get; set; }
            [JsonProperty("date_accepted")] public DateTime? DateAccepted { get; set; }
            [JsonProperty("date_completed")] public DateTime? DateCompleted { get; set; }
            [JsonProperty("days_to_complete")] public int? DaysToComplete { get; set; }
            [JsonProperty("price")] public decimal? Price { get; set; }
            [JsonProperty("reward")] public decimal? Reward { get; set; }
            [JsonProperty("collateral")] public decimal? Collateral { get; set; }
            [JsonProperty("buyout")] public decimal? Buyout { get; set; }
            [JsonProperty("volume")] public double? Volume { get; set; }
        }

        private sealed class EsiContractBid
        {
            [JsonProperty("bid_id")] public long BidID { get; set; }
            [JsonProperty("bidder_id")] public long BidderID { get; set; }
            [JsonProperty("amount")] public decimal Amount { get; set; }
            [JsonProperty("date_bid")] public DateTime DateBid { get; set; }
        }

        private sealed class EsiContractItem
        {
            [JsonProperty("record_id")] public long RecordID { get; set; }
            [JsonProperty("type_id")] public int TypeID { get; set; }
            [JsonProperty("quantity")] public long Quantity { get; set; }
            [JsonProperty("raw_quantity")] public int? RawQuantity { get; set; }
            [JsonProperty("is_singleton")] public bool IsSingleton { get; set; }
            [JsonProperty("is_included")] public bool IsIncluded { get; set; }
        }

        private sealed class EsiAsset
        {
            [JsonProperty("item_id")] public long ItemID { get; set; }
            [JsonProperty("location_id")] public long LocationID { get; set; }
            [JsonProperty("location_flag")] public string LocationFlag { get; set; }
            [JsonProperty("type_id")] public int TypeID { get; set; }
            [JsonProperty("quantity")] public long Quantity { get; set; }
            [JsonProperty("is_singleton")] public bool IsSingleton { get; set; }
            [JsonProperty("is_blueprint_copy")] public bool? IsBlueprintCopy { get; set; }
        }

        private sealed class EsiMailHeader
        {
            [JsonProperty("mail_id")] public long MailID { get; set; }
            [JsonProperty("from")] public long From { get; set; }
            [JsonProperty("subject")] public string Subject { get; set; }
            [JsonProperty("timestamp")] public DateTime Timestamp { get; set; }
            [JsonProperty("recipients")] public List<EsiMailRecipient> Recipients { get; set; }
        }

        private sealed class EsiMailRecipient
        {
            [JsonProperty("recipient_id")] public long RecipientID { get; set; }
            [JsonProperty("recipient_type")] public string RecipientType { get; set; }
        }

        private sealed class EsiMailingList
        {
            [JsonProperty("mailing_list_id")] public long MailingListID { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
        }

        private sealed class EsiMail
        {
            [JsonProperty("body")] public string Body { get; set; }
        }

        private sealed class EsiNotification
        {
            [JsonProperty("notification_id")] public long NotificationID { get; set; }
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("sender_id")] public long SenderID { get; set; }
            [JsonProperty("sender_type")] public string SenderType { get; set; }
            [JsonProperty("timestamp")] public DateTime Timestamp { get; set; }
            [JsonProperty("is_read")] public bool? IsRead { get; set; }
            [JsonProperty("text")] public string Text { get; set; }
        }

        #endregion
    }
}
