using HermesProxy.Enums;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server.Systems;

/// <summary>
/// Translation for the modern client's calendar CMSGs.
/// </summary>
/// <remarks>
/// Only the pending-invite count so far, which the client asks for at login to light the minimap
/// calendar button. The answer, SMSG_CALENDAR_SEND_NUM_PENDING, is one uint32 on both sides.
/// </remarks>
public static class CalendarSystem
{
    [HandlesCmsg(Opcode.CMSG_CALENDAR_GET_NUM_PENDING)]
    public static void HandleCalendarGetNumPending(in EmptyClientPacket request, in SessionContext ctx)
    {
        if (LegacyVersion.RemovedInVersion(ClientVersionBuild.V3_0_2_9056))
            return; // no calendar before WotLK

        WorldPacket packet = new WorldPacket(Opcode.CMSG_CALENDAR_GET_NUM_PENDING);
        ctx.SendPacketToServer(packet);
    }
}
