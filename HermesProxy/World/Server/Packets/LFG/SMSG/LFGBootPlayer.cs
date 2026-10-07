using HermesProxy.World.Enums;
using System.Text;

namespace HermesProxy.World.Server.Packets;

public class LFGBootPlayer : ServerPacket
{
    public bool VoteInProgress;
    public bool VotePassed;
    public bool MyVoteCompleted;
    public bool MyVote;
    public WowGuid128 Target;
    public uint TotalVotes;
    public uint BootVotes;
    public int TimeLeft;
    public uint VotesNeeded;
    public string Reason = string.Empty;

    public LFGBootPlayer() : base(Opcode.SMSG_LFG_BOOT_PLAYER) { }

    public override void Write()
    {
        _worldPacket.WriteBit(VoteInProgress);
        _worldPacket.WriteBit(VotePassed);
        _worldPacket.WriteBit(MyVoteCompleted);
        _worldPacket.WriteBit(MyVote);
        _worldPacket.WriteBits(Encoding.UTF8.GetByteCount(Reason), 9);
        _worldPacket.FlushBits();
        _worldPacket.WritePackedGuid128(Target);
        _worldPacket.WriteUInt32(TotalVotes);
        _worldPacket.WriteUInt32(BootVotes);
        _worldPacket.WriteInt32(TimeLeft);
        _worldPacket.WriteUInt32(VotesNeeded);
        _worldPacket.WriteString(Reason);
    }
}
