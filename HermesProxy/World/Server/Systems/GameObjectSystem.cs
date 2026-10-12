using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server.Systems;

/// <summary>
/// Game-object interaction CMSGs.
/// </summary>
/// <remarks>
/// The two look alike and are not. Use asks the server to use the object. Report-use follows
/// every interaction the client allowed, whether or not use was sent: a locked object the client
/// will not open still gets one. A 3.x server answers it with the object's gossip hello script and
/// its use-object achievement criteria, which is how Loken's Pedestal summons the Image of Loken.
/// </remarks>
public static class GameObjectSystem
{
    [HandlesCmsg(Opcode.CMSG_GAME_OBJ_USE)]
    public static void HandleGameObjUse(in GameObjUse use, in SessionContext ctx)
    {
        WorldPacket packet = new WorldPacket(Opcode.CMSG_GAME_OBJ_USE);
        packet.WriteGuid(use.Guid.To64());
        ctx.SendPacketToServer(packet);
    }

    /// <summary>
    /// Records the object for the mail handlers and forwards the report to a server that has it
    /// (3.0.2 on); an older one has nothing to tell.
    /// </summary>
    [HandlesCmsg(Opcode.CMSG_GAME_OBJ_REPORT_USE)]
    public static void HandleGameObjReportUse(in GameObjReportUse use, in SessionContext ctx)
    {
        ctx.GetSession().GameState.CurrentInteractedWithGO = use.Guid;
        if (LegacyVersion.GetCurrentOpcode(Opcode.CMSG_GAME_OBJ_REPORT_USE) == 0)
            return;

        WorldPacket packet = new WorldPacket(Opcode.CMSG_GAME_OBJ_REPORT_USE);
        packet.WriteGuid(use.Guid.To64());
        ctx.SendPacketToServer(packet);
    }
}
