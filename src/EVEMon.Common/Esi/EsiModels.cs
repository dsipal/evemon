using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace EVEMon.Common.Esi
{
    // Response models of the ESI routes EVEMon uses (https://esi.evetech.net/ui/).
    // Optional values are nullable, as ESI omits them.

    internal sealed class EsiCharacterPublicInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("birthday")] public DateTime Birthday { get; set; }
        [JsonProperty("gender")] public string Gender { get; set; }
        [JsonProperty("race_id")] public int RaceID { get; set; }
        [JsonProperty("bloodline_id")] public int BloodlineID { get; set; }
        [JsonProperty("ancestry_id")] public int? AncestryID { get; set; }
        [JsonProperty("corporation_id")] public long CorporationID { get; set; }
        [JsonProperty("alliance_id")] public long? AllianceID { get; set; }
        [JsonProperty("faction_id")] public int? FactionID { get; set; }
        [JsonProperty("security_status")] public double? SecurityStatus { get; set; }
    }

    internal sealed class EsiSkills
    {
        [JsonProperty("skills")] public List<EsiSkill> Skills { get; set; } = new List<EsiSkill>();
        [JsonProperty("total_sp")] public long TotalSP { get; set; }
        [JsonProperty("unallocated_sp")] public int? UnallocatedSP { get; set; }
    }

    internal sealed class EsiSkill
    {
        [JsonProperty("skill_id")] public int SkillID { get; set; }
        [JsonProperty("skillpoints_in_skill")] public long SkillPoints { get; set; }
        [JsonProperty("trained_skill_level")] public int TrainedLevel { get; set; }
        [JsonProperty("active_skill_level")] public int ActiveLevel { get; set; }
    }

    internal sealed class EsiAttributes
    {
        [JsonProperty("intelligence")] public int Intelligence { get; set; }
        [JsonProperty("memory")] public int Memory { get; set; }
        [JsonProperty("perception")] public int Perception { get; set; }
        [JsonProperty("willpower")] public int Willpower { get; set; }
        [JsonProperty("charisma")] public int Charisma { get; set; }
        [JsonProperty("bonus_remaps")] public int? BonusRemaps { get; set; }
        [JsonProperty("last_remap_date")] public DateTime? LastRemapDate { get; set; }
        [JsonProperty("accrued_remap_cooldown_date")] public DateTime? AccruedRemapCooldownDate { get; set; }
    }

    internal sealed class EsiClones
    {
        [JsonProperty("home_location")] public EsiCloneLocation HomeLocation { get; set; }
        [JsonProperty("jump_clones")] public List<EsiJumpClone> JumpClones { get; set; } = new List<EsiJumpClone>();
        [JsonProperty("last_clone_jump_date")] public DateTime? LastCloneJumpDate { get; set; }
        [JsonProperty("last_station_change_date")] public DateTime? LastStationChangeDate { get; set; }
    }

    internal sealed class EsiCloneLocation
    {
        [JsonProperty("location_id")] public long LocationID { get; set; }
        [JsonProperty("location_type")] public string LocationType { get; set; }
    }

    internal sealed class EsiJumpClone
    {
        [JsonProperty("jump_clone_id")] public long JumpCloneID { get; set; }
        [JsonProperty("location_id")] public long LocationID { get; set; }
        [JsonProperty("location_type")] public string LocationType { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("implants")] public List<int> Implants { get; set; } = new List<int>();
    }

    internal sealed class EsiFatigue
    {
        [JsonProperty("jump_fatigue_expire_date")] public DateTime? JumpFatigueExpireDate { get; set; }
        [JsonProperty("last_jump_date")] public DateTime? LastJumpDate { get; set; }
        [JsonProperty("last_update_date")] public DateTime? LastUpdateDate { get; set; }
    }

    internal sealed class EsiLocation
    {
        [JsonProperty("solar_system_id")] public int SolarSystemID { get; set; }
        [JsonProperty("station_id")] public long? StationID { get; set; }
        [JsonProperty("structure_id")] public long? StructureID { get; set; }
    }

    internal sealed class EsiShip
    {
        [JsonProperty("ship_item_id")] public long ShipItemID { get; set; }
        [JsonProperty("ship_name")] public string ShipName { get; set; }
        [JsonProperty("ship_type_id")] public int ShipTypeID { get; set; }
    }

    internal sealed class EsiCorporationHistoryItem
    {
        [JsonProperty("record_id")] public long RecordID { get; set; }
        [JsonProperty("corporation_id")] public int CorporationID { get; set; }
        [JsonProperty("start_date")] public DateTime StartDate { get; set; }
    }

    internal sealed class EsiSkillQueueItem
    {
        [JsonProperty("skill_id")] public int SkillID { get; set; }
        [JsonProperty("finished_level")] public int FinishedLevel { get; set; }
        [JsonProperty("queue_position")] public int QueuePosition { get; set; }
        [JsonProperty("start_date")] public DateTime? StartDate { get; set; }
        [JsonProperty("finish_date")] public DateTime? FinishDate { get; set; }
        [JsonProperty("training_start_sp")] public int? TrainingStartSP { get; set; }
        [JsonProperty("level_start_sp")] public int? LevelStartSP { get; set; }
        [JsonProperty("level_end_sp")] public int? LevelEndSP { get; set; }
    }

    internal sealed class EsiName
    {
        [JsonProperty("id")] public long ID { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("category")] public string Category { get; set; }
    }

    internal sealed class EsiStructure
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("solar_system_id")] public int SolarSystemID { get; set; }
    }

    internal sealed class EsiRace
    {
        [JsonProperty("race_id")] public int RaceID { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }

    internal sealed class EsiBloodline
    {
        [JsonProperty("bloodline_id")] public int BloodlineID { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }

    internal sealed class EsiAncestry
    {
        [JsonProperty("id")] public int ID { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }
}
