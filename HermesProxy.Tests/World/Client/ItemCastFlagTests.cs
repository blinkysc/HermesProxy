using HermesProxy.World.Client;
using HermesProxy.World.Objects;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// Retail 3.4 marks a cast an item makes with <c>CastFlag.CastItem</c>, except a spell the item
/// casts passively by being worn or carried. Without the flag the 3.4.3 client left Rune of
/// Command's cast open after its go, hands glowing until the quest took the item (capture of
/// 2026-10-11 16:54:55).
/// </summary>
public class ItemCastFlagTests
{
    private const int RuneOfCommand = 49859;

    private static ItemTemplate Item(int spellId, int trigger)
    {
        var template = new ItemTemplate();
        template.TriggeredSpellIds[0] = spellId;
        template.TriggeredSpellTypes[0] = trigger;
        return template;
    }

    [Theory]
    [InlineData(0)] // on use
    [InlineData(2)] // chance on hit
    public void UsedOrProcdItemSpell_IsMarked(int trigger)
        => Assert.True(WorldClient.IsUsedItemSpell(Item(RuneOfCommand, trigger), RuneOfCommand));

    [Theory]
    [InlineData(1)] // on equip: Alchemist's Stone
    [InlineData(5)] // carried: Haunted Memento
    public void PassiveItemSpell_IsNotMarked(int trigger)
        => Assert.False(WorldClient.IsUsedItemSpell(Item(RuneOfCommand, trigger), RuneOfCommand));

    [Fact]
    public void ItemNotQueriedYet_IsMarked()
        => Assert.True(WorldClient.IsUsedItemSpell(null, RuneOfCommand));

    [Fact]
    public void SpellTheItemDoesNotList_IsMarked()
        => Assert.True(WorldClient.IsUsedItemSpell(Item(17619, 1), RuneOfCommand));
}
