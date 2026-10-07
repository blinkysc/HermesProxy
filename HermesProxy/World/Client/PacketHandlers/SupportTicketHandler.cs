using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Client;

public partial class WorldClient
{
    // Legacy 3.3.5a answers with a uint32 status; modern wants an int32. Same single value, so
    // the backend's own GMTicketSystemStatus config reaches the client unchanged.
    [HandlesSmsg(Opcode.SMSG_GM_TICKET_GET_SYSTEM_STATUS)]
    internal void HandleGmTicketGetSystemStatus(WorldPacket packet)
    {
        GMTicketSystemStatus status = new();
        status.Status = (int)packet.ReadUInt32();
        SendPacketToClient(status);
    }

    [HandlesSmsg(Opcode.SMSG_GM_TICKET_CREATE)]
    internal void HandleGmTicketCreate(WorldPacket packet)
    {
        var response = (LegacyGmTicketResponse) packet.ReadUInt32();
        bool isError = !(response is LegacyGmTicketResponse.CreateSuccess or LegacyGmTicketResponse.UpdateSuccess);
        if (response == LegacyGmTicketResponse.AlreadyExist)
        {
            Session.SendHermesTextMessage("You already have an open GM ticket; the server takes one at a time, so this was not filed.", isError);
            return;
        }
        Session.SendHermesTextMessage($"GM Ticket Status: {response}", isError);
    }

    [HandlesSmsg(Opcode.SMSG_COMPLAINT_RESULT)]
    internal void HandleComplaintResult(WorldPacket packet)
    {
        ComplaintResult result = new();
        result.ComplaintType = GetSession().GameState.LastComplaintSpamType; // the legacy answer has no type
        result.Result = packet.ReadUInt8();
        SendPacketToClient(result);
    }

    // A GM answered the ticket. The modern client's ticket UI has no place for the answer, so it
    // goes to chat, and the ticket is closed as a native client does once it was read.
    [HandlesSmsg(Opcode.SMSG_GMRESPONSE_RECEIVED)]
    internal void HandleGmResponseReceived(WorldPacket packet)
    {
        packet.ReadUInt32(); // response id
        packet.ReadUInt32(); // ticket id
        packet.ReadCString(); // the ticket text
        var answer = new System.Text.StringBuilder();
        for (int i = 0; i < 4 && packet.CanRead(); i++)
            answer.Append(packet.ReadCString());
        Session.SendHermesTextMessage($"A GM answered your ticket: {answer}");
        SendPacketToServer(new WorldPacket(Opcode.CMSG_GM_TICKET_RESPONSE_RESOLVE));
    }
}
