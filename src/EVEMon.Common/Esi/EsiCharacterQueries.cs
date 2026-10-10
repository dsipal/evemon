using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EVEMon.Common.Data;
using EVEMon.Common.Serialization.Eve;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// Builds the character data EVEMon used to get from the XML API, from the equivalent ESI routes.
    /// The results are the same serialization objects the XML API produced, so the model imports them unchanged.
    /// </summary>
    internal static partial class EsiCharacterQueries
    {
        /// <summary>
        /// Character sheet: bio, attributes, skills, implants, jump clones, wallet balance and jump fatigue.
        /// </summary>
        internal static async Task<EsiQueryResult> CharacterSheetAsync(EsiQueryContext context)
        {
            long id = context.CharacterID;
            string token = context.AccessToken;

            Task<EsiResponse<EsiCharacterPublicInfo>> infoTask = EsiClient.GetAsync<EsiCharacterPublicInfo>($"/characters/{id}/");
            Task<EsiResponse<EsiSkills>> skillsTask = EsiClient.GetAsync<EsiSkills>($"/characters/{id}/skills/", token);
            Task<EsiResponse<EsiAttributes>> attributesTask = EsiClient.GetAsync<EsiAttributes>($"/characters/{id}/attributes/", token);
            Task<EsiResponse<List<int>>> implantsTask = EsiClient.GetAsync<List<int>>($"/characters/{id}/implants/", token);
            Task<EsiResponse<EsiClones>> clonesTask = EsiClient.GetAsync<EsiClones>($"/characters/{id}/clones/", token);
            Task<EsiResponse<decimal>> walletTask = EsiClient.GetAsync<decimal>($"/characters/{id}/wallet/", token);
            Task<EsiResponse<EsiFatigue>> fatigueTask = EsiClient.GetAsync<EsiFatigue>($"/characters/{id}/fatigue/", token);

            await Task.WhenAll(infoTask, skillsTask, attributesTask, implantsTask, clonesTask, walletTask, fatigueTask)
                .ConfigureAwait(false);

            EsiCharacterPublicInfo info = infoTask.Result.Result;
            EsiSkills skills = skillsTask.Result.Result;
            EsiAttributes attributes = attributesTask.Result.Result;
            EsiClones clones = clonesTask.Result.Result;
            EsiFatigue fatigue = fatigueTask.Result.Result;

            IDictionary<long, string> names = await EsiNameResolver.GetNamesAsync(new[]
            {
                info.CorporationID,
                info.AllianceID ?? 0,
                info.FactionID ?? 0
            }).ConfigureAwait(false);

            Tuple<string, string, string> origin = await EsiNameResolver
                .GetOriginNamesAsync(info.RaceID, info.BloodlineID, info.AncestryID).ConfigureAwait(false);

            SerializableAPICharacterSheet sheet = new SerializableAPICharacterSheet
            {
                ID = id,
                Name = info.Name,
                Birthday = info.Birthday,
                Race = origin.Item1,
                BloodLine = origin.Item2,
                Ancestry = origin.Item3,
                Gender = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(info.Gender ?? String.Empty),
                CorporationID = info.CorporationID,
                CorporationName = GetName(names, info.CorporationID),
                AllianceID = info.AllianceID ?? 0,
                AllianceName = GetName(names, info.AllianceID ?? 0),
                FactionID = info.FactionID ?? 0,
                FactionName = GetName(names, info.FactionID ?? 0),
                SecurityStatus = info.SecurityStatus ?? 0,
                Balance = walletTask.Result.Result,
                FreeSkillPoints = skills.UnallocatedSP ?? 0,
                HomeStationID = clones.HomeLocation?.LocationID ?? 0,
                CloneJumpDate = clones.LastCloneJumpDate ?? DateTime.MinValue,
                RemoteStationDate = clones.LastStationChangeDate ?? DateTime.MinValue,
                FreeRespecs = (short)(attributes.BonusRemaps ?? 0),
                LastRespecDate = attributes.LastRemapDate ?? DateTime.MinValue,
                // ESI gives when the next yearly remap becomes available; EVEMon stores when the last one was used
                LastTimedRespec = attributes.AccruedRemapCooldownDate?.AddYears(-1) ?? DateTime.MinValue,
                JumpActivationDate = fatigue.LastJumpDate ?? DateTime.MinValue,
                JumpFatigueDate = fatigue.JumpFatigueExpireDate ?? DateTime.MinValue,
                JumpLastUpdateDate = fatigue.LastUpdateDate ?? DateTime.MinValue,
                Attributes = new SerializableCharacterAttributes
                {
                    Intelligence = attributes.Intelligence,
                    Memory = attributes.Memory,
                    Perception = attributes.Perception,
                    Willpower = attributes.Willpower,
                    Charisma = attributes.Charisma
                }
            };

            foreach (EsiSkill skill in skills.Skills)
            {
                sheet.Skills.Add(new SerializableCharacterSkill
                {
                    ID = skill.SkillID,
                    Name = StaticSkills.GetSkillByID(skill.SkillID)?.Name ?? String.Empty,
                    Level = skill.TrainedLevel,
                    Skillpoints = skill.SkillPoints,
                    IsKnown = true
                });
            }

            // Implants are matched by name when imported
            foreach (int implantID in implantsTask.Result.Result)
            {
                sheet.Implants.Add(new SerializableNewImplant { ID = implantID, Name = GetItemName(implantID) });
            }

            foreach (EsiJumpClone jumpClone in clones.JumpClones)
            {
                string location = await EsiNameResolver.GetLocationNameAsync(jumpClone.LocationID, token).ConfigureAwait(false);

                sheet.JumpClones.Add(new SerializableCharacterJumpClone
                {
                    JumpCloneID = jumpClone.JumpCloneID,
                    LocationID = jumpClone.LocationID,
                    CloneName = String.IsNullOrWhiteSpace(jumpClone.Name)
                        ? $"Clone in {location}"
                        : $"{jumpClone.Name} ({location})"
                });

                foreach (int implantID in jumpClone.Implants)
                {
                    sheet.JumpCloneImplants.Add(new SerializableCharacterJumpCloneImplant
                    {
                        JumpCloneID = jumpClone.JumpCloneID,
                        TypeID = implantID,
                        TypeName = GetItemName(implantID)
                    });
                }
            }

            return new EsiQueryResult(sheet, LatestExpiry(infoTask.Result.Expires, skillsTask.Result.Expires, attributesTask.Result.Expires, implantsTask.Result.Expires, clonesTask.Result.Expires, walletTask.Result.Expires, fatigueTask.Result.Expires), skillsTask.Result.ServerTime);
        }

        /// <summary>
        /// Character info: ship, location, security status and employment history.
        /// </summary>
        internal static async Task<EsiQueryResult> CharacterInfoAsync(EsiQueryContext context)
        {
            long id = context.CharacterID;
            string token = context.AccessToken;

            Task<EsiResponse<EsiCharacterPublicInfo>> infoTask = EsiClient.GetAsync<EsiCharacterPublicInfo>($"/characters/{id}/");
            Task<EsiResponse<EsiLocation>> locationTask = EsiClient.GetAsync<EsiLocation>($"/characters/{id}/location/", token);
            Task<EsiResponse<EsiShip>> shipTask = EsiClient.GetAsync<EsiShip>($"/characters/{id}/ship/", token);
            Task<EsiResponse<List<EsiCorporationHistoryItem>>> historyTask =
                EsiClient.GetAsync<List<EsiCorporationHistoryItem>>($"/characters/{id}/corporationhistory/");

            await Task.WhenAll(infoTask, locationTask, shipTask, historyTask).ConfigureAwait(false);

            EsiLocation location = locationTask.Result.Result;
            EsiShip ship = shipTask.Result.Result;

            // EVEMon resolves the location by name: an NPC station when docked in one, otherwise the solar system
            // (Upwell structures are not in the static data, so a character docked in one shows its system)
            long locationID = location.StationID ?? location.SolarSystemID;
            string locationName = await EsiNameResolver.GetLocationNameAsync(locationID, token).ConfigureAwait(false);

            IDictionary<long, string> corporationNames = await EsiNameResolver
                .GetNamesAsync(historyTask.Result.Result.Select(x => (long)x.CorporationID)).ConfigureAwait(false);

            SerializableAPICharacterInfo characterInfo = new SerializableAPICharacterInfo
            {
                ShipName = ship.ShipName,
                ShipTypeName = GetItemName(ship.ShipTypeID),
                LastKnownLocation = locationName,
                SecurityStatus = infoTask.Result.Result.SecurityStatus ?? 0
            };

            foreach (EsiCorporationHistoryItem record in historyTask.Result.Result.OrderByDescending(x => x.StartDate))
            {
                characterInfo.EmploymentHistory.Add(new SerializableEmploymentHistoryListItem
                {
                    RecordID = record.RecordID,
                    CorporationID = record.CorporationID,
                    CorporationName = GetName(corporationNames, record.CorporationID),
                    StartDate = record.StartDate
                });
            }

            return new EsiQueryResult(characterInfo, LatestExpiry(infoTask.Result.Expires, locationTask.Result.Expires, shipTask.Result.Expires, historyTask.Result.Expires), locationTask.Result.ServerTime);
        }

        /// <summary>
        /// Skill queue.
        /// </summary>
        internal static async Task<EsiQueryResult> SkillQueueAsync(EsiQueryContext context)
        {
            EsiResponse<List<EsiSkillQueueItem>> response = await EsiClient
                .GetAsync<List<EsiSkillQueueItem>>($"/characters/{context.CharacterID}/skillqueue/", context.AccessToken)
                .ConfigureAwait(false);

            SerializableAPISkillQueue queue = new SerializableAPISkillQueue();
            foreach (EsiSkillQueueItem item in response.Result
                .Where(x => !x.FinishDate.HasValue || x.FinishDate > response.ServerTime)
                .OrderBy(x => x.QueuePosition))
            {
                SerializableQueuedSkill queuedSkill = new SerializableQueuedSkill
                {
                    ID = item.SkillID,
                    Level = item.FinishedLevel,
                    StartSP = item.TrainingStartSP ?? item.LevelStartSP ?? 0,
                    EndSP = item.LevelEndSP ?? 0,
                    CCPStartTime = String.Empty,
                    CCPEndTime = String.Empty
                };

                // A paused queue has no dates, which the model detects through empty times
                if (item.StartDate.HasValue && item.FinishDate.HasValue)
                {
                    queuedSkill.StartTime = item.StartDate.Value;
                    queuedSkill.EndTime = item.FinishDate.Value;
                }

                queue.Queue.Add(queuedSkill);
            }

            return new EsiQueryResult(queue, response.Expires, response.ServerTime);
        }

        #region Helpers

        private static string GetName(IDictionary<long, string> names, long id)
        {
            string name;
            return id > 0 && names.TryGetValue(id, out name) ? name : String.Empty;
        }

        private static string GetItemName(int typeID) => StaticItems.GetItemByID(typeID)?.Name ?? $"Type {typeID}";

        private static DateTime LatestExpiry(params DateTime[] expiries) => expiries.Max();

        #endregion
    }
}
