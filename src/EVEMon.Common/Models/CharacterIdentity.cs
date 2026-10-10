using System;
using System.Linq;
using EVEMon.Common.Enumerations.CCPAPI;
using EVEMon.Common.Esi;

namespace EVEMon.Common.Models
{
    /// <summary>
    /// Represents a character's identity, defined by its sole ID. 
    /// Character identities to ensure 
    /// </summary>
    public sealed class CharacterIdentity
    {
        /// <summary>
        /// Constructor from an id and a name.
        /// </summary>
        /// <param name="id">The id for this identity</param>
        /// <param name="name">The name.</param>
        /// <param name="corpId">The corp id.</param>
        /// <param name="corpName">Name of the corp.</param>
        /// <param name="allianceId"></param>
        /// <param name="allianceName"></param>
        /// <param name="factionId"></param>
        /// <param name="factionName"></param>
        internal CharacterIdentity(long id, string name, long corpId, string corpName, long allianceId, string allianceName,
            int factionId, string factionName)
        {
            CharacterID = id;
            CharacterName = name;
            CorporationID = corpId;
            CorporationName = corpName;
            AllianceID = allianceId;
            AllianceName = allianceName;
            FactionID = factionId;
            FactionName = factionName;
        }

        /// <summary>
        /// Gets the character ID.
        /// </summary>
        /// <value>The character ID.</value>
        public long CharacterID { get; }

        /// <summary>
        /// Gets the character's name.
        /// </summary>
        /// <value>The name of the character.</value>
        public string CharacterName { get; internal set; }

        /// <summary>
        /// Gets or sets the corporation ID.
        /// </summary>
        /// <value>The corporation ID.</value>
        public long CorporationID { get; set; }

        /// <summary>
        /// Gets or sets the name of the corporation.
        /// </summary>
        /// <value>The name of the corporation.</value>
        public string CorporationName { get; set; }

        /// <summary>
        /// Gets or sets the alliance identifier.
        /// </summary>
        /// <value>
        /// The alliance identifier.
        /// </value>
        public long AllianceID { get; set; }

        /// <summary>
        /// Gets or sets the name of the alliance.
        /// </summary>
        /// <value>
        /// The name of the alliance.
        /// </value>
        public string AllianceName { get; set; }

        /// <summary>
        /// Gets or sets the faction identifier.
        /// </summary>
        /// <value>
        /// The faction identifier.
        /// </value>
        public int FactionID { get; set; }

        /// <summary>
        /// Gets or sets the name of the faction.
        /// </summary>
        /// <value>
        /// The name of the faction.
        /// </value>
        public string FactionName { get; set; }

        /// <summary>
        /// Gets the ESI key (EVE SSO authorization) of this character, or null if it has none.
        /// </summary>
        public ESIKey ESIKey { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether this instance can query character related info.
        /// </summary>
        public bool CanQueryCharacterInfo => ESIKey != null && ESIKey.Monitored;

        /// <summary>
        /// Gets a value indicating whether this instance can query corporation related info.
        /// </summary>
        /// <remarks>On ESI, corporation data is read with the character's token and in-game roles.</remarks>
        public bool CanQueryCorporationInfo => ESIKey != null && ESIKey.Monitored &&
                                               new Enum[]
                                               {
                                                   CCPAPICorporationMethods.CorporationMedals,
                                                   CCPAPICorporationMethods.CorporationMarketOrders,
                                                   CCPAPICorporationMethods.CorporationContracts,
                                                   CCPAPICorporationMethods.CorporationIndustryJobs
                                               }.Any(method => EsiQueries.HasAccess(ESIKey, method));

        /// <summary>
        /// Gets the CCP character representing this identity, or null when there is none.
        /// </summary>
        public CCPCharacter CCPCharacter
            => EveMonClient.Characters.OfType<CCPCharacter>()
                .FirstOrDefault(character => character.CharacterID == CharacterID);

        /// <summary>
        /// Finds the ESI key with access to the specified method.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <returns>
        /// The ESI key, or null if the character has none, if it lacks the needed scopes,
        /// or if the method is not available on ESI yet.
        /// </returns>
        public ESIKey FindESIKeyWithAccess(Enum method) => EsiQueries.HasAccess(ESIKey, method) ? ESIKey : null;
    }
}