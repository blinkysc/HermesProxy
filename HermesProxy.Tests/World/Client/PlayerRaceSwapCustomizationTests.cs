using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

// mod-cfbg (AzerothCore cross-faction battlegrounds) puts a Horde player on the Alliance side by
// rewriting the race byte of UNIT_FIELD_BYTES_0 mid-session. PLAYER_BYTES/PLAYER_BYTES_2 are left
// alone, so the modern customization choices — which are per race — must be rebuilt from the
// cached bytes or the client is handed a Human wearing Tauren choice IDs.
public class PlayerRaceSwapCustomizationTests
{
    const byte Skin = 3, Face = 2, HairStyle = 4, HairColor = 1, FacialHair = 2;

    [Fact]
    public void RaceSwap_RebuildsCustomizationsForTheNewRace()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 91);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 90));
        Create(harness, legacyGuid, Race.Tauren);

        var update = DeliverBytes0(harness, legacyGuid, Race.Human);

        Assert.NotNull(update.PlayerData);
        Assert.True(update.PlayerData.HasCustomizationsUpdate);
        var expected = CharacterCustomizations.ConvertLegacyCustomizationsToModern(Race.Human, Gender.Male,
            Skin, Face, HairStyle, HairColor, FacialHair);
        Assert.Equal(expected.Select(c => (c.ChrCustomizationOptionID, c.ChrCustomizationChoiceID)),
            update.PlayerData.Customizations.ToArray().Take(expected.Count).Select(c => (c!.ChrCustomizationOptionID, c.ChrCustomizationChoiceID)));
    }

    [Fact]
    public void Bytes0WithSameRace_LeavesCustomizationsAlone()
    {
        // A druid shifting form moves DisplayPower in the same field; nothing to rebuild.
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 92);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 90));
        Create(harness, legacyGuid, Race.Tauren);

        var update = DeliverBytes0(harness, legacyGuid, Race.Tauren, displayPower: 1);

        Assert.False(update.PlayerData?.HasCustomizationsUpdate ?? false);
    }

    [Fact]
    public void IndexPastTheNewRacesOptions_FallsBackToItsFirstChoice()
    {
        // Tauren have more skin colors than Humans; index 18 has no Human record.
        uint first = CharacterCustomizations.GetModernCustomizationChoice(Race.Human, Gender.Male, LegacyCustomizationOption.Skin, 0);
        Assert.Equal(0u, CharacterCustomizations.GetModernCustomizationChoice(Race.Human, Gender.Male, LegacyCustomizationOption.Skin, 18));

        var choices = CharacterCustomizations.ConvertLegacyCustomizationsToModern(Race.Human, Gender.Male, 18, 0, 0, 0, 0);

        Assert.NotEqual(0u, first);
        Assert.Equal(first, choices[0].ChrCustomizationChoiceID);
    }

    static uint Bytes0(Race race, byte displayPower = 0)
        => (uint)race | ((uint)Class.Warrior << 8) | ((uint)Gender.Male << 16) | ((uint)displayPower << 24);

    static void Create(LegacyHandlerHarness harness, WowGuid64 legacyGuid, Race race)
    {
        var guid = legacyGuid.To128(harness.Session.GameState);
        var update = new ObjectUpdate(guid, UpdateTypeModern.CreateObject1, harness.Session);
        update.CreateData.ObjectType = ObjectType.Player;
        var bytes = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            packet.WriteUInt32(1);
            LegacyPacketBuilder.WriteValuesBlock(packet, legacyGuid, LegacyVersion.GetUpdateField(PlayerField.PLAYER_END),
                (LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BYTES_0), Bytes0(race)),
                (LegacyVersion.GetUpdateField(PlayerField.PLAYER_BYTES),
                    Skin | ((uint)Face << 8) | ((uint)HairStyle << 16) | ((uint)HairColor << 24)),
                (LegacyVersion.GetUpdateField(PlayerField.PLAYER_BYTES_2), FacialHair));
        });
        using var legacy = LegacyPacketBuilder.Receive(bytes);
        legacy.ReadUInt32();
        legacy.ReadUInt8();
        legacy.ReadPackedGuid();
        harness.Client.ReadValuesUpdateBlockOnCreate(legacy, ref guid, ObjectType.Player, update,
            new AuraUpdate(guid, true), 0);
    }

    static ObjectUpdate DeliverBytes0(LegacyHandlerHarness harness, WowGuid64 legacyGuid, Race race, byte displayPower = 0)
    {
        harness.ClientWire.Sent.Clear();
        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
            {
                packet.WriteUInt32(1);
                LegacyPacketBuilder.WriteValuesBlock(packet, legacyGuid, LegacyVersion.GetUpdateField(PlayerField.PLAYER_END),
                    (LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BYTES_0), Bytes0(race, displayPower)));
            }),
            harness.Client.HandleUpdateObject);
        var packet = Assert.Single(harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>());
        return Assert.Single(packet.ObjectUpdates);
    }
}
