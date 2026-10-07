using System.Collections.Generic;
using System.Linq;
using Framework.Logging;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server;

public class CurrentPlayerStorage
{
    private readonly GlobalSessionData _globalSession;
    public CompletedQuestTracker CompletedQuests { get; private set; } = null!;
    public PlayerSettings Settings { get; private set; } = null!;

    public CurrentPlayerStorage(GlobalSessionData globalSession)
    {
        _globalSession = globalSession;
    }

    // Build fully-loaded instances into locals and only then publish them. The modern
    // client opens the instance socket on its own network thread while this runs, and
    // its first packets (CMSG_SET_ACTION_BAR_TOGGLES, guild settings) touch these
    // objects, and publishing a half-Reloaded instance is a live NullReferenceException.
    public void LoadCurrentPlayer()
    {
        var quests = new CompletedQuestTracker(_globalSession);
        var settings = new PlayerSettings(_globalSession);
        quests.Reload();
        settings.Reload();

        CompletedQuests = quests;
        Settings = settings;
    }
}

public class PlayerSettings
{
    private InternalStorage _internalStorage = new();
    private PlayerFlags _lastCapturedFlags;

    public bool NeedToForcePatchFlags { get; private set; }


    public GlobalSessionData Session { get; }

    public PlayerSettings(GlobalSessionData globalSession)
    {
        Session = globalSession;
    }

    public bool AutoBlockGuildInvites => _internalStorage.AutoBlockGuildInvites;

    public void SetAutoBlockGuildInvites(bool value)
    {
        _internalStorage.AutoBlockGuildInvites = value;
        NeedToForcePatchFlags = true;
        Save();
    }

    public void PatchFlags(ref PlayerFlags flags)
    {
        _lastCapturedFlags = flags;
        NeedToForcePatchFlags = false;

        if (_internalStorage.AutoBlockGuildInvites)
            flags |= PlayerFlags.AutoDeclineGuild;
        else
            flags &= ~(PlayerFlags.AutoDeclineGuild);
    }

    public PlayerFlags CreateNewFlags()
    {
        var flags = _lastCapturedFlags;
        PatchFlags(ref flags);
        return flags;
    }

    private void Save()
    {
        Session.AccountMetaDataMgr.SaveCharacterSettingsStorage(Session.GameState.CurrentPlayerInfo!.Realm.Name, Session.GameState.CurrentPlayerInfo!.Name!, _internalStorage);
    }
    
    public class InternalStorage
    {
        // A JSON encoder / decoder is used to store the settings
        // Make use of a public { get; set; } Property so that the JSON serializer can change it

        // The player can request a change in the Interface settings
        // but the actual value has to be reflected in the local CharacterFlags
        public bool AutoBlockGuildInvites { get; set; }
        public uint LastSummonedPetSpecies { get; set; }
    }

    public uint LastSummonedPetSpecies => _internalStorage.LastSummonedPetSpecies;

    public void SetLastSummonedPetSpecies(uint speciesId)
    {
        if (_internalStorage.LastSummonedPetSpecies == speciesId)
            return;
        _internalStorage.LastSummonedPetSpecies = speciesId;
        Save();
    }

    public void Reload()
    {
        _internalStorage = Session.AccountMetaDataMgr.LoadCharacterSettingsStorage(Session.GameState.CurrentPlayerInfo!.Realm.Name, Session.GameState.CurrentPlayerInfo!.Name!);
    }
}

public class CompletedQuestTracker
{
    private Dictionary<int, ulong> _cachedQuestCompleted = new();
    private bool _playerCreated; // the client has the player object (WriteAllCompletedIntoArray ran)

    public GlobalSessionData Session { get; }

    public CompletedQuestTracker(GlobalSessionData globalSession)
    {
        Session = globalSession;
    }

    public void MarkQuestAsNotCompleted(uint questQuestId)
    {
        Session.AccountMetaDataMgr.MarkQuestAsNotCompleted(Session.GameState.CurrentPlayerInfo!.Realm.Name, Session.GameState.CurrentPlayerInfo!.Name!, questQuestId);

        var questBit = GameData.GetUniqueQuestBit(questQuestId);
        if (questBit.HasValue)
        {
            SendSingleUpdateToClient(questBit.Value, false);
        }
    }

    public void MarkQuestAsCompleted(uint questQuestId)
    {
        // A plain repeatable can be taken again straight away; flagging it completed hid it.
        if (GameData.GetQuestReset(questQuestId) == QuestReset.Repeatable)
            return;

        Session.AccountMetaDataMgr.MarkQuestAsCompleted(Session.GameState.CurrentPlayerInfo!.Realm.Name, Session.GameState.CurrentPlayerInfo!.Name!, questQuestId);

        var questBit = GameData.GetUniqueQuestBit(questQuestId);
        if (questBit.HasValue)
        {
            SendSingleUpdateToClient(questBit.Value, true);
        }
    }

    /// <summary>
    /// Replaces the whole set with the server's list (3.3.5a SMSG_QUERY_QUESTS_COMPLETED_RESPONSE),
    /// saves it, and - if the client already has the player object - sends the words that changed.
    /// Before that, the player's create update picks the new set up via WriteAllCompletedIntoArray.
    /// </summary>
    public void ReplaceAll(IReadOnlyCollection<uint> questIds)
    {
        var kept = FilterRepeatables(questIds, Session.GameState.DailyQuestsDone.Values);
        Session.AccountMetaDataMgr.SetAllCompletedQuests(Session.GameState.CurrentPlayerInfo!.Realm.Name, Session.GameState.CurrentPlayerInfo!.Name!, kept);

        var old = _cachedQuestCompleted;
        Reload();
        var changed = old.Keys.Union(_cachedQuestCompleted.Keys)
            .Where(k => old.GetValueOrDefault(k) != _cachedQuestCompleted.GetValueOrDefault(k))
            .ToList();
        Log.Print(LogType.Server, $"[Quests] server reports {questIds.Count} completed quests, {questIds.Count - kept.Count} repeatable left out ({changed.Count} QuestCompleted words changed{(_playerCreated ? "" : ", applied at player create")})");
        if (!_playerCreated || changed.Count == 0)
            return;

        ObjectUpdate updateData = new ObjectUpdate(Session.GameState.CurrentPlayerGuid, UpdateTypeModern.Values, Session);
        ulong?[] words = updateData.EnsureActivePlayerData().EnsureQuestCompleted();
        foreach (int idx in changed)
            words[idx] = _cachedQuestCompleted.GetValueOrDefault(idx);

        UpdateObject updatePacket = new UpdateObject(Session.GameState);
        updatePacket.ObjectUpdates.Add(updateData);
        Session.WorldClient!.SendPlayerValuesUpdate(updatePacket);
    }

    /// <summary>
    /// The quests to keep flagged completed: the server lists every quest ever rewarded, including
    /// repeatables that can be taken again, which the client would then hide. A repeatable stays
    /// only while it is one of today's dailies.
    /// </summary>
    public static List<uint> FilterRepeatables(IEnumerable<uint> questIds, IEnumerable<uint> doneToday)
    {
        var today = new HashSet<uint>(doneToday);
        var kept = new List<uint>();
        foreach (uint questId in questIds)
        {
            if (GameData.GetQuestReset(questId) == QuestReset.None || today.Contains(questId))
                kept.Add(questId);
        }
        return kept;
    }

    /// <summary>A daily slot emptied at the reset: the quest can be done again.</summary>
    public void OnDailyQuestReset(uint questId)
    {
        if (questId != 0 && IsTracked(questId))
            MarkQuestAsNotCompleted(questId);
    }

    private bool IsTracked(uint questId)
    {
        uint? questBit = GameData.GetUniqueQuestBit(questId);
        if (!questBit.HasValue)
            return false;
        int idx = (int)((questBit.Value - 1) >> 6);
        return _cachedQuestCompleted.TryGetValue(idx, out ulong word)
            && (word & (1UL << (int)((questBit.Value - 1) & 63))) != 0;
    }

    public void Reload()
    {
        var questIds = Session.AccountMetaDataMgr.GetAllCompletedQuests(Session.GameState.CurrentPlayerInfo!.Realm.Name, Session.GameState.CurrentPlayerInfo!.Name!);

        _cachedQuestCompleted = new Dictionary<int, ulong>();
        foreach (uint questId in questIds)
        {
            uint? questBit = GameData.GetUniqueQuestBit(questId);
            if (!questBit.HasValue)
                continue;

            int idx = (int)(((questBit - 1) >> 6));
            int bitIdx = (int)((questBit - 1) & 63);
            _cachedQuestCompleted.TryAdd(idx, 0);
            _cachedQuestCompleted[idx] |= ((ulong)1) << bitIdx;
        }
    }
    
    private void SendSingleUpdateToClient(uint questBit, bool isSet)
    {
        int idx = (int)(((questBit - 1) >> 6));
        int bitIdx = (int)((questBit - 1) & 63);
        _cachedQuestCompleted.TryAdd(idx, 0);
        if (isSet)
            _cachedQuestCompleted[idx] |= ((ulong)1) << bitIdx;
        else
            _cachedQuestCompleted[idx] &= ~(((ulong)1) << bitIdx);
        
        ObjectUpdate updateData = new ObjectUpdate(Session.GameState.CurrentPlayerGuid, UpdateTypeModern.Values, Session);
        updateData.EnsureActivePlayerData().EnsureQuestCompleted()[idx] = _cachedQuestCompleted[idx];

        UpdateObject updatePacket = new UpdateObject(Session.GameState);
        updatePacket.ObjectUpdates.Add(updateData);
        Session.WorldClient!.SendPlayerValuesUpdate(updatePacket);
    }

    /// <summary>
    /// Stamps the whole set onto a login update. Takes the owner block rather than its array so a
    /// character with nothing completed never materialises the 14 KB <c>QuestCompleted[875]</c> —
    /// which is every character on a backend that cannot report quest history in the first place.
    /// </summary>
    public void WriteAllCompletedIntoArray(ActivePlayerData dest)
    {
        _playerCreated = true;
        if (_cachedQuestCompleted.Count == 0)
            return;

        ulong?[] questCompleted = dest.EnsureQuestCompleted();
        foreach (var kv in _cachedQuestCompleted)
        {
            questCompleted[kv.Key] = kv.Value;
        }
    }
}
