using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

public class GameObjectReportUseTests
{
    /// <summary>
    /// Loken's Pedestal: the client won't open its lock, and only reports the use. The 3.3.5a
    /// server summons the Image of Loken from that report, so it has to arrive.
    /// </summary>
    [Fact]
    public void ReportUse_ReachesTheServerWithTheObjectsGuid()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, null, harness.Client);
        WowGuid128 pedestal = WowGuid128.Create(HighGuidType703.GameObject, 0, 188596, 9431);

        GameObjectSystem.HandleGameObjReportUse(new GameObjReportUse(pedestal), in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(LegacyVersion.GetCurrentOpcode(Opcode.CMSG_GAME_OBJ_REPORT_USE), sent.Opcode);
        Assert.Equal(new byte[] { 0xd7, 0x24, 0x00, 0xb4, 0xe0, 0x02, 0x10, 0xf1 }, sent.Bytes[^8..]);
        Assert.Equal(pedestal, harness.Session.GameState.CurrentInteractedWithGO);
    }
}
