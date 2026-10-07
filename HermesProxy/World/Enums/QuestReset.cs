namespace HermesProxy.World.Enums;

/// <summary>How often a quest can be done again, from <c>QuestRepeatable_N.csv</c>.</summary>
public enum QuestReset : byte
{
    None,
    Daily,
    Weekly,
    Monthly,
    Seasonal,
    Repeatable,
}
