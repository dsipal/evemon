using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EVEMon.Common.Data;
using EVEMon.Common.Serialization.Eve;
using Newtonsoft.Json;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Kill log, planetary colonies and calendar.
    /// </summary>
    internal static partial class EsiCharacterQueries
    {
        // Each killmail is a separate request; killmails never change, so they are cached
        private const int MaxKillmails = 100;
        private static readonly ConcurrentDictionary<long, EsiKillmail> s_killmails = new ConcurrentDictionary<long, EsiKillmail>();

        #region Kill log

        /// <summary>
        /// The character's recent kills and losses.
        /// </summary>
        internal static async Task<EsiQueryResult> KillLogAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiKillmailReference>> response = await EsiClient
                .GetAllPagesAsync<EsiKillmailReference>($"/characters/{context.CharacterID}/killmails/recent/",
                    context.AccessToken)
                .ConfigureAwait(false);

            List<EsiKillmail> killmails = new List<EsiKillmail>();
            foreach (EsiKillmailReference reference in response.Result.OrderByDescending(x => x.KillmailID).Take(MaxKillmails))
            {
                EsiKillmail killmail;
                if (!s_killmails.TryGetValue(reference.KillmailID, out killmail))
                {
                    killmail = (await EsiClient
                        .GetAsync<EsiKillmail>($"/killmails/{reference.KillmailID}/{reference.KillmailHash}/")
                        .ConfigureAwait(false)).Result;
                    s_killmails[reference.KillmailID] = killmail;
                }
                killmails.Add(killmail);
            }

            IDictionary<long, string> names = await EsiNameResolver.GetNamesAsync(killmails.SelectMany(x =>
                    new[] { x.Victim }.Cast<EsiKillmailParticipant>().Concat(x.Attackers))
                .SelectMany(x => new[] { x.CharacterID ?? 0, x.CorporationID ?? 0, x.AllianceID ?? 0, x.FactionID ?? 0 }))
                .ConfigureAwait(false);

            SerializableAPIKillLog killLog = new SerializableAPIKillLog();
            foreach (EsiKillmail killmail in killmails)
            {
                SerializableKillLogListItem kill = new SerializableKillLogListItem
                {
                    KillID = killmail.KillmailID,
                    SolarSystemID = killmail.SolarSystemID,
                    KillTime = killmail.KillmailTime,
                    MoonID = killmail.MoonID ?? 0,
                    Victim = new SerializableKillLogVictim { DamageTaken = killmail.Victim.DamageTaken }
                };
                FillParticipant(kill.Victim, killmail.Victim, names);

                foreach (EsiKillmailAttacker attacker in killmail.Attackers)
                {
                    SerializableKillLogAttackersListItem item = new SerializableKillLogAttackersListItem
                    {
                        DamageDone = attacker.DamageDone,
                        FinalBlow = attacker.FinalBlow,
                        SecurityStatus = attacker.SecurityStatus,
                        WeaponTypeID = attacker.WeaponTypeID ?? 0
                    };
                    FillParticipant(item, attacker, names);
                    kill.Attackers.Add(item);
                }

                AddKillmailItems(kill.Items, killmail.Victim.Items);
                killLog.Kills.Add(kill);
            }

            return new EsiQueryResult(killLog, response.Expires, response.ServerTime);
        }

        private static void FillParticipant(SerializableCharacterListItem target, EsiKillmailParticipant source,
            IDictionary<long, string> names)
        {
            target.ID = source.CharacterID ?? 0;
            target.Name = GetName(names, source.CharacterID ?? 0);
            target.CorporationID = source.CorporationID ?? 0;
            target.CorporationName = GetName(names, source.CorporationID ?? 0);
            target.AllianceID = source.AllianceID ?? 0;
            target.AllianceName = GetName(names, source.AllianceID ?? 0);
            target.FactionID = source.FactionID ?? 0;
            target.FactionName = GetName(names, source.FactionID ?? 0);
            target.ShipTypeID = source.ShipTypeID ?? 0;
        }

        private static void AddKillmailItems(ICollection<SerializableKillLogItemListItem> target,
            IEnumerable<EsiKillmailItem> items)
        {
            if (items == null)
                return;

            foreach (EsiKillmailItem item in items)
            {
                SerializableKillLogItemListItem killItem = new SerializableKillLogItemListItem
                {
                    TypeID = item.ItemTypeID,
                    EVEFlag = (short)item.Flag,
                    QtyDropped = (int)(item.QuantityDropped ?? 0),
                    QtyDestroyed = (int)(item.QuantityDestroyed ?? 0),
                    Singleton = (byte)item.Singleton
                };
                AddKillmailItems(killItem.Items, item.Items);
                target.Add(killItem);
            }
        }

        #endregion


        #region Planetary interaction

        /// <summary>
        /// Planetary colonies.
        /// </summary>
        internal static async Task<EsiQueryResult> PlanetaryColoniesAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiColony>> response = await EsiClient
                .GetAsync<List<EsiColony>>($"/characters/{context.CharacterID}/planets/", context.AccessToken)
                .ConfigureAwait(false);

            string ownerName = await EsiNameResolver.GetNameAsync(context.CharacterID).ConfigureAwait(false);

            SerializableAPIPlanetaryColonies colonies = new SerializableAPIPlanetaryColonies();
            foreach (EsiColony colony in response.Result)
            {
                EsiPlanet planet = (await EsiClient.GetAsync<EsiPlanet>($"/universe/planets/{colony.PlanetID}/")
                    .ConfigureAwait(false)).Result;

                colonies.Colonies.Add(new SerializablePlanetaryColony
                {
                    PlanetID = colony.PlanetID,
                    PlanetName = planet?.Name ?? $"Planet {colony.PlanetID}",
                    PlanetTypeID = planet?.TypeID ?? 0,
                    PlanetTypeName = planet != null ? GetItemName(planet.TypeID) : EsiCharacterQueries.ToTitleWords(colony.PlanetType),
                    SolarSystemID = colony.SolarSystemID,
                    SolarSystemName = SolarSystemName(colony.SolarSystemID),
                    OwnerID = colony.OwnerID,
                    OwnerName = ownerName ?? String.Empty,
                    LastUpdate = colony.LastUpdate,
                    UpgradeLevel = colony.UpgradeLevel,
                    NumberOfPins = colony.NumberOfPins
                });
            }

            return new EsiQueryResult(colonies, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Pins of a colony (<see cref="EsiQueryContext.ID"/> is the planet ID).
        /// </summary>
        internal static async Task<EsiQueryResult> PlanetaryPinsAsync(EsiQueryContext context)
        {
            EsiResponse<EsiColonyLayout> response = await GetColonyLayoutAsync(context).ConfigureAwait(false);

            SerializableAPIPlanetaryPins pins = new SerializableAPIPlanetaryPins();
            foreach (EsiPin pin in response.Result.Pins ?? new List<EsiPin>())
            {
                EsiPinContent content = pin.Contents?.FirstOrDefault();
                int contentTypeID = pin.ExtractorDetails?.ProductTypeID ?? content?.TypeID ?? 0;

                pins.Pins.Add(new SerializablePlanetaryPin
                {
                    PinID = pin.PinID,
                    TypeID = pin.TypeID,
                    TypeName = GetItemName(pin.TypeID),
                    SchematicID = pin.SchematicID ?? pin.FactoryDetails?.SchematicID ?? 0,
                    // The XML API gave the extraction cycle in minutes
                    CycleTime = (short)((pin.ExtractorDetails?.CycleTime ?? 0) / 60),
                    QuantityPerCycle = pin.ExtractorDetails?.QuantityPerCycle ?? 0,
                    ContentTypeID = contentTypeID,
                    ContentTypeName = contentTypeID > 0 ? GetItemName(contentTypeID) : String.Empty,
                    ContentQuantity = (int)(content?.Amount ?? 0),
                    Longitude = pin.Longitude,
                    Latitude = pin.Latitude,
                    LastLaunchTime = pin.LastCycleStart ?? DateTime.MinValue,
                    InstallTime = pin.InstallTime ?? DateTime.MinValue,
                    ExpiryTime = pin.ExpiryTime ?? DateTime.MinValue
                });
            }

            return new EsiQueryResult(pins, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Routes of a colony (<see cref="EsiQueryContext.ID"/> is the planet ID).
        /// </summary>
        internal static async Task<EsiQueryResult> PlanetaryRoutesAsync(EsiQueryContext context)
        {
            EsiResponse<EsiColonyLayout> response = await GetColonyLayoutAsync(context).ConfigureAwait(false);

            SerializableAPIPlanetaryRoutes routes = new SerializableAPIPlanetaryRoutes();
            foreach (EsiRoute route in response.Result.Routes ?? new List<EsiRoute>())
            {
                List<long> waypoints = route.Waypoints ?? new List<long>();
                routes.Routes.Add(new SerializablePlanetaryRoute
                {
                    RouteID = route.RouteID,
                    SourcePinID = route.SourcePinID,
                    DestinationPinID = route.DestinationPinID,
                    ContentTypeID = route.ContentTypeID,
                    ContentTypeName = GetItemName(route.ContentTypeID),
                    Quantity = (int)route.Quantity,
                    Waypoint1 = waypoints.ElementAtOrDefault(0),
                    Waypoint2 = waypoints.ElementAtOrDefault(1),
                    Waypoint3 = waypoints.ElementAtOrDefault(2),
                    Waypoint4 = waypoints.ElementAtOrDefault(3),
                    Waypoint5 = waypoints.ElementAtOrDefault(4)
                });
            }

            return new EsiQueryResult(routes, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Links of a colony (<see cref="EsiQueryContext.ID"/> is the planet ID).
        /// </summary>
        internal static async Task<EsiQueryResult> PlanetaryLinksAsync(EsiQueryContext context)
        {
            EsiResponse<EsiColonyLayout> response = await GetColonyLayoutAsync(context).ConfigureAwait(false);

            SerializableAPIPlanetaryLinks links = new SerializableAPIPlanetaryLinks();
            foreach (EsiLink link in response.Result.Links ?? new List<EsiLink>())
            {
                links.Links.Add(new SerializablePlanetaryLink
                {
                    SourcePinID = link.SourcePinID,
                    DestinationPinID = link.DestinationPinID,
                    LinkLevel = (short)link.LinkLevel
                });
            }

            return new EsiQueryResult(links, response.Expires, response.ServerTime);
        }

        private static Task<EsiResponse<EsiColonyLayout>> GetColonyLayoutAsync(EsiQueryContext context)
            => EsiClient.GetAsync<EsiColonyLayout>($"/characters/{context.CharacterID}/planets/{context.ID}/",
                context.AccessToken);

        #endregion


        #region Calendar

        /// <summary>
        /// Upcoming calendar events, with their details.
        /// </summary>
        internal static async Task<EsiQueryResult> UpcomingCalendarEventsAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiCalendarEventSummary>> response = await EsiClient
                .GetAsync<List<EsiCalendarEventSummary>>($"/characters/{context.CharacterID}/calendar/", context.AccessToken)
                .ConfigureAwait(false);

            SerializableAPIUpcomingCalendarEvents events = new SerializableAPIUpcomingCalendarEvents();
            foreach (EsiCalendarEventSummary summary in response.Result)
            {
                EsiCalendarEvent details = (await EsiClient
                    .GetAsync<EsiCalendarEvent>($"/characters/{context.CharacterID}/calendar/{summary.EventID}/",
                        context.AccessToken)
                    .ConfigureAwait(false)).Result;

                events.UpcomingEvents.Add(new SerializableUpcomingCalendarEventsListItem
                {
                    EventID = summary.EventID,
                    OwnerID = details.OwnerID,
                    OwnerName = details.OwnerName ?? String.Empty,
                    EventTitle = details.Title ?? summary.Title ?? String.Empty,
                    EventText = details.Text ?? String.Empty,
                    Duration = details.Duration,
                    Importance = details.Importance > 0,
                    Response = GetCalendarResponse(details.Response ?? summary.EventResponse),
                    EventDate = details.Date
                });
            }

            return new EsiQueryResult(events, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Attendees of a calendar event (<see cref="EsiQueryContext.ID"/> is the event ID).
        /// </summary>
        internal static async Task<EsiQueryResult> CalendarEventAttendeesAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiCalendarAttendee>> response = await EsiClient
                .GetAsync<List<EsiCalendarAttendee>>(
                    $"/characters/{context.CharacterID}/calendar/{context.ID}/attendees/", context.AccessToken)
                .ConfigureAwait(false);

            IDictionary<long, string> names = await EsiNameResolver
                .GetNamesAsync(response.Result.Select(x => x.CharacterID)).ConfigureAwait(false);

            SerializableAPICalendarEventAttendees attendees = new SerializableAPICalendarEventAttendees();
            foreach (EsiCalendarAttendee attendee in response.Result)
            {
                attendees.EventAttendees.Add(new SerializableCalendarEventAttendeeListItem
                {
                    CharacterID = attendee.CharacterID,
                    CharacterName = GetName(names, attendee.CharacterID),
                    Response = GetCalendarResponse(attendee.EventResponse)
                });
            }

            return new EsiQueryResult(attendees, response.Expires, response.ServerTime);
        }

        /// <summary>
        /// Turns an ESI event response into the XML API one.
        /// </summary>
        private static string GetCalendarResponse(string response)
        {
            switch (response)
            {
                case "accepted":
                    return "Accepted";
                case "declined":
                    return "Declined";
                case "tentative":
                    return "Tentative";
                default:
                    return "Undecided";
            }
        }

        #endregion


        #region ESI models

        private sealed class EsiKillmailReference
        {
            [JsonProperty("killmail_id")] public long KillmailID { get; set; }
            [JsonProperty("killmail_hash")] public string KillmailHash { get; set; }
        }

        private sealed class EsiKillmail
        {
            [JsonProperty("killmail_id")] public long KillmailID { get; set; }
            [JsonProperty("killmail_time")] public DateTime KillmailTime { get; set; }
            [JsonProperty("solar_system_id")] public int SolarSystemID { get; set; }
            [JsonProperty("moon_id")] public int? MoonID { get; set; }
            [JsonProperty("victim")] public EsiKillmailVictim Victim { get; set; }
            [JsonProperty("attackers")] public List<EsiKillmailAttacker> Attackers { get; set; } = new List<EsiKillmailAttacker>();
        }

        private class EsiKillmailParticipant
        {
            [JsonProperty("character_id")] public long? CharacterID { get; set; }
            [JsonProperty("corporation_id")] public long? CorporationID { get; set; }
            [JsonProperty("alliance_id")] public long? AllianceID { get; set; }
            [JsonProperty("faction_id")] public int? FactionID { get; set; }
            [JsonProperty("ship_type_id")] public int? ShipTypeID { get; set; }
        }

        private sealed class EsiKillmailVictim : EsiKillmailParticipant
        {
            [JsonProperty("damage_taken")] public int DamageTaken { get; set; }
            [JsonProperty("items")] public List<EsiKillmailItem> Items { get; set; }
        }

        private sealed class EsiKillmailAttacker : EsiKillmailParticipant
        {
            [JsonProperty("damage_done")] public int DamageDone { get; set; }
            [JsonProperty("final_blow")] public bool FinalBlow { get; set; }
            [JsonProperty("security_status")] public double SecurityStatus { get; set; }
            [JsonProperty("weapon_type_id")] public int? WeaponTypeID { get; set; }
        }

        private sealed class EsiKillmailItem
        {
            [JsonProperty("item_type_id")] public int ItemTypeID { get; set; }
            [JsonProperty("flag")] public int Flag { get; set; }
            [JsonProperty("quantity_dropped")] public long? QuantityDropped { get; set; }
            [JsonProperty("quantity_destroyed")] public long? QuantityDestroyed { get; set; }
            [JsonProperty("singleton")] public int Singleton { get; set; }
            [JsonProperty("items")] public List<EsiKillmailItem> Items { get; set; }
        }

        private sealed class EsiColony
        {
            [JsonProperty("planet_id")] public long PlanetID { get; set; }
            [JsonProperty("planet_type")] public string PlanetType { get; set; }
            [JsonProperty("solar_system_id")] public int SolarSystemID { get; set; }
            [JsonProperty("owner_id")] public long OwnerID { get; set; }
            [JsonProperty("last_update")] public DateTime LastUpdate { get; set; }
            [JsonProperty("upgrade_level")] public int UpgradeLevel { get; set; }
            [JsonProperty("num_pins")] public int NumberOfPins { get; set; }
        }

        private sealed class EsiPlanet
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("type_id")] public int TypeID { get; set; }
        }

        private sealed class EsiColonyLayout
        {
            [JsonProperty("pins")] public List<EsiPin> Pins { get; set; }
            [JsonProperty("routes")] public List<EsiRoute> Routes { get; set; }
            [JsonProperty("links")] public List<EsiLink> Links { get; set; }
        }

        private sealed class EsiPin
        {
            [JsonProperty("pin_id")] public long PinID { get; set; }
            [JsonProperty("type_id")] public int TypeID { get; set; }
            [JsonProperty("schematic_id")] public int? SchematicID { get; set; }
            [JsonProperty("latitude")] public double Latitude { get; set; }
            [JsonProperty("longitude")] public double Longitude { get; set; }
            [JsonProperty("install_time")] public DateTime? InstallTime { get; set; }
            [JsonProperty("expiry_time")] public DateTime? ExpiryTime { get; set; }
            [JsonProperty("last_cycle_start")] public DateTime? LastCycleStart { get; set; }
            [JsonProperty("contents")] public List<EsiPinContent> Contents { get; set; }
            [JsonProperty("extractor_details")] public EsiExtractorDetails ExtractorDetails { get; set; }
            [JsonProperty("factory_details")] public EsiFactoryDetails FactoryDetails { get; set; }
        }

        private sealed class EsiPinContent
        {
            [JsonProperty("type_id")] public int TypeID { get; set; }
            [JsonProperty("amount")] public long Amount { get; set; }
        }

        private sealed class EsiExtractorDetails
        {
            [JsonProperty("cycle_time")] public int? CycleTime { get; set; }
            [JsonProperty("qty_per_cycle")] public int? QuantityPerCycle { get; set; }
            [JsonProperty("product_type_id")] public int? ProductTypeID { get; set; }
        }

        private sealed class EsiFactoryDetails
        {
            [JsonProperty("schematic_id")] public int SchematicID { get; set; }
        }

        private sealed class EsiRoute
        {
            [JsonProperty("route_id")] public long RouteID { get; set; }
            [JsonProperty("source_pin_id")] public long SourcePinID { get; set; }
            [JsonProperty("destination_pin_id")] public long DestinationPinID { get; set; }
            [JsonProperty("content_type_id")] public int ContentTypeID { get; set; }
            [JsonProperty("quantity")] public double Quantity { get; set; }
            [JsonProperty("waypoints")] public List<long> Waypoints { get; set; }
        }

        private sealed class EsiLink
        {
            [JsonProperty("source_pin_id")] public long SourcePinID { get; set; }
            [JsonProperty("destination_pin_id")] public long DestinationPinID { get; set; }
            [JsonProperty("link_level")] public int LinkLevel { get; set; }
        }

        private sealed class EsiCalendarEventSummary
        {
            [JsonProperty("event_id")] public long EventID { get; set; }
            [JsonProperty("title")] public string Title { get; set; }
            [JsonProperty("event_response")] public string EventResponse { get; set; }
        }

        private sealed class EsiCalendarEvent
        {
            [JsonProperty("date")] public DateTime Date { get; set; }
            [JsonProperty("duration")] public int Duration { get; set; }
            [JsonProperty("importance")] public int Importance { get; set; }
            [JsonProperty("owner_id")] public long OwnerID { get; set; }
            [JsonProperty("owner_name")] public string OwnerName { get; set; }
            [JsonProperty("response")] public string Response { get; set; }
            [JsonProperty("text")] public string Text { get; set; }
            [JsonProperty("title")] public string Title { get; set; }
        }

        private sealed class EsiCalendarAttendee
        {
            [JsonProperty("character_id")] public long CharacterID { get; set; }
            [JsonProperty("event_response")] public string EventResponse { get; set; }
        }

        #endregion
    }
}
