using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World.Enums;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// The sound of a dismissed pet reaches the client: model id and position, the same layout on
/// 3.3.5a (AzerothCore's PetDismissSound) and the modern client.
/// </summary>
public class PetDismissSoundTests
{
    [Fact]
    public void DismissSound_IsForwardedWithModelAndPosition()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_PET_DISMISS_SOUND, p =>
        {
            p.WriteInt32(1132);
            p.WriteFloat(1913.5f);
            p.WriteFloat(-4127.25f);
            p.WriteFloat(43.5f);
        });

        harness.Deliver(Opcode.SMSG_PET_DISMISS_SOUND, wire, harness.Client.HandlePetDismissSound);

        var sent = Assert.Single(harness.ClientWire.Sent);
        Assert.Equal(Opcode.SMSG_PET_DISMISS_SOUND, sent.Opcode);
        byte[] body = [
            .. BitConverter.GetBytes(1132),
            .. BitConverter.GetBytes(1913.5f),
            .. BitConverter.GetBytes(-4127.25f),
            .. BitConverter.GetBytes(43.5f),
        ];
        Assert.Equal(body, sent.Bytes.Skip(sent.Bytes.Length - body.Length).ToArray());
        Assert.Equal(body.Length, ((HermesProxy.World.Server.Packets.PetDismissSound)sent.Packet).MaxSize);
    }
}
