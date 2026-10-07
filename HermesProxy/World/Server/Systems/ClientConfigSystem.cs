using Framework.Logging;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Logging;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Server.Systems;

/// <summary>
/// Account-data and CUF-profile CMSGs: the client's own saved settings.
/// </summary>
/// <remarks>
/// The proxy keeps the client's blobs in <c>AccountDataMgr</c> and answers the client from there.
/// On a 3.x server the types both clients share (config, bindings, macros: 0-5) are also saved on
/// the server, so they follow the character between the proxy and a native 3.3.5a client, and
/// the newer of the two copies wins.
/// </remarks>
public static class ClientConfigSystem
{
    // The legacy server reads the size as a uint16-sized buffer (AccountData max 0xFFFF).
    private const uint LegacyMaxUncompressedSize = 0xFFFF;

    private static readonly Microsoft.Extensions.Logging.ILogger _melLog = Log.CreateMelLogger(Log.CategoryServer);

    [HandlesCmsg(Opcode.CMSG_UPDATE_ACCOUNT_DATA)]
    public static void HandleUpdateAccountData(in UserClientUpdateAccountData data, in SessionContext ctx)
    {
        byte[] compressed = data.CompressedData;
        var accountData = ctx.GetSession().AccountDataMgr;
        accountData.SaveData(data.PlayerGuid, data.Time, data.DataType, data.Size, compressed);

        if (!AccountDataManager.IsSyncedWithServer(data.DataType))
            return;
        if (data.Size > LegacyMaxUncompressedSize)
        {
            AccountDataLogMessages.TooLargeForServer(_melLog, data.DataType, data.Size);
            return;
        }

        WorldPacket packet = new WorldPacket(Opcode.CMSG_UPDATE_ACCOUNT_DATA);
        packet.WriteUInt32(data.DataType);
        packet.WriteUInt32((uint)data.Time);
        packet.WriteUInt32(data.Size);
        if (data.Size != 0)
            packet.WriteBytes(compressed);
        ctx.SendPacketToServer(packet);
        accountData.ServerTimes[data.DataType] = (uint)data.Time;
        AccountDataLogMessages.SavedToServer(_melLog, data.DataType, data.Time, data.Size);
    }

    [HandlesCmsg(Opcode.CMSG_REQUEST_ACCOUNT_DATA)]
    public static void HandleRequestAccountData(in RequestAccountData data, in SessionContext ctx)
    {
        var accountData = ctx.GetSession().AccountDataMgr;
        if (accountData.ServerIsNewer(data.DataType))
        {
            // Answered by WorldClient.HandleUpdateAccountData once the server sends its copy.
            AccountDataLogMessages.FetchingFromServer(_melLog, data.DataType, accountData.ServerTimes[data.DataType],
                accountData.Data[data.DataType]?.Timestamp ?? 0);
            accountData.AwaitServerData(data.DataType, data.PlayerGuid);
            WorldPacket request = new WorldPacket(Opcode.CMSG_REQUEST_ACCOUNT_DATA);
            request.WriteUInt32(data.DataType);
            ctx.SendPacketToServer(request);
            return;
        }

        if (accountData.Data[data.DataType] == null)
        {
            Log.Print(LogType.Error, $"Client requested missing account data {data.DataType}.");
            accountData.Data[data.DataType] = new();
            accountData.Data[data.DataType].Type = data.DataType;
            accountData.Data[data.DataType].Timestamp = Time.UnixTime;
            accountData.Data[data.DataType].UncompressedSize = 0;
            accountData.Data[data.DataType].CompressedData = new byte[0];
        }

        accountData.Data[data.DataType].Guid = data.PlayerGuid;
        AccountData stored = accountData.Data[data.DataType];

        UpdateAccountData update = new(stored);
        ctx.SendPacket(update);
    }

    [HandlesCmsg(Opcode.CMSG_SAVE_CUF_PROFILES)]
    public static void HandleSaveCUFProfiles(in SaveCUFProfiles cuf, in SessionContext ctx)
    {
        ctx.GetSession().AccountDataMgr.SaveCUFProfiles(cuf.Data);
    }
}
