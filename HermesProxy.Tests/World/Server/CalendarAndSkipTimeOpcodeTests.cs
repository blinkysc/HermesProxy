using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using Xunit;
using V343 = HermesProxy.World.Enums.V3_4_3_54261;

namespace HermesProxy.Tests.World.Server;

public class CalendarAndSkipTimeOpcodeTests
{
    [Fact]
    public void V343_MappedFromWpp()
    {
        // WPP V3_4_3_51666, which agrees with every value the 54261 table already had.
        Assert.Equal(11800u, (uint)V343.Opcode.SMSG_MOVE_SKIP_TIME);              // 0x2E18
        Assert.Equal(9884u, (uint)V343.Opcode.SMSG_CALENDAR_SEND_NUM_PENDING);    // 0x269C
        Assert.Equal(13948u, (uint)V343.Opcode.CMSG_CALENDAR_GET_NUM_PENDING);
    }

    [Fact]
    public void CalendarSendNumPending_Write_IsOneUInt32()
    {
        var pending = new CalendarSendNumPending { NumPending = 3 };
        pending.WritePacketData();
        byte[] body = pending.GetData()!;

        Assert.Equal(4, body.Length);
        using var reader = new WorldPacket(Frame(body));
        Assert.Equal(3u, reader.ReadUInt32());
    }

    static byte[] Frame(byte[] body)
    {
        var framed = new byte[body.Length + 2];
        body.CopyTo(framed, 2);
        return framed;
    }
}
