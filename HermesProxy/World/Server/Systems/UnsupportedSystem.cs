using Framework.Logging;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Logging;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server.Systems;

/// <summary>
/// CMSGs the proxy drops on purpose, because a legacy server has nothing they could be turned into.
/// </summary>
/// <remarks>
/// Without a handler each of these logged "No handler for opcode" at Warning, every login, and hid
/// the gaps that matter. Dropping one is what the client already got; a native server either
/// ignores them too or answers with data a legacy server does not have. Something that can be
/// translated or answered belongs in its domain's system, not here.
/// </remarks>
public static class UnsupportedSystem
{
    private static readonly Microsoft.Extensions.Logging.ILogger _melLog = Log.CreateMelLogger(Log.CategoryPacket);
    private static readonly string _sourceFile = nameof(WorldSocket).PadRight(15);
    private static readonly string _netDirRecv = Log.FormatDir(LogNetDir.C2P);

    // In-game shop and paid services.
    [HandlesCmsg(Opcode.CMSG_BATTLE_PAY_GET_PRODUCT_LIST)]
    [HandlesCmsg(Opcode.CMSG_BATTLE_PAY_GET_PURCHASE_LIST)]
    [HandlesCmsg(Opcode.CMSG_UPDATE_VAS_PURCHASE_STATES)]
    [HandlesCmsg(Opcode.CMSG_GET_UNDELETE_CHARACTER_COOLDOWN_STATUS)]
    [HandlesCmsg(Opcode.CMSG_SOCIAL_CONTRACT_REQUEST)]
    // Client telemetry and settings Blizzard collects; no 3.3.5a counterpart.
    [HandlesCmsg(Opcode.CMSG_REPORT_CLIENT_VARIABLES)]
    [HandlesCmsg(Opcode.CMSG_REPORT_ENABLED_ADDONS)]
    [HandlesCmsg(Opcode.CMSG_REPORT_KEYBINDING_EXECUTION_COUNTS)]
    [HandlesCmsg(Opcode.CMSG_VIOLENCE_LEVEL)]
    [HandlesCmsg(Opcode.CMSG_OVERRIDE_SCREEN_FLASH)]
    [HandlesCmsg(Opcode.CMSG_QUEUED_MESSAGES_END)]
    // Requests the 3.3.5a server covers another way, or a feature it lacks: forced reactions are
    // pushed at login, start timers are world states, and there are no guild achievements, graveyard
    // choice or PvP reward caps to query.
    [HandlesCmsg(Opcode.CMSG_REQUEST_FORCED_REACTIONS)]
    [HandlesCmsg(Opcode.CMSG_QUERY_COUNTDOWN_TIMER)]
    [HandlesCmsg(Opcode.CMSG_GUILD_SET_ACHIEVEMENT_TRACKING)]
    [HandlesCmsg(Opcode.CMSG_REQUEST_CEMETERY_LIST)]
    [HandlesCmsg(Opcode.CMSG_REQUEST_PVP_REWARDS)]
    public static void HandleUnsupported(Opcode opcode, in UnsupportedClientPacket packet, in SessionContext ctx)
    {
        WorldSocketLogMessages.UnsupportedClientPacketDropped(_melLog, _sourceFile, _netDirRecv, opcode);
    }
}
