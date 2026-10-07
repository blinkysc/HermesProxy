using Microsoft.Extensions.Logging;

namespace HermesProxy.World.Logging;

/// <summary>
/// Logging for account data (keybindings, macros, UI layout) synced with a 3.x legacy server.
/// </summary>
/// <remarks>EventId 1620-1629 is reserved for this file.</remarks>
internal static partial class AccountDataLogMessages
{
    [LoggerMessage(EventId = 1620, Level = LogLevel.Debug,
        Message = "[AccountData] server times mask=0x{Mask:X2} forwarded={Forwarded}")]
    public static partial void ServerTimes(ILogger logger, uint mask, bool forwarded);

    [LoggerMessage(EventId = 1621, Level = LogLevel.Information,
        Message = "[AccountData] type {Type}: server copy is newer ({ServerTime} > {LocalTime}), fetching it")]
    public static partial void FetchingFromServer(ILogger logger, uint type, long serverTime, long localTime);

    [LoggerMessage(EventId = 1622, Level = LogLevel.Information,
        Message = "[AccountData] type {Type}: received from server (time {Time}, {Size} bytes, converted from 3.3.5a: {Converted})")]
    public static partial void ReceivedFromServer(ILogger logger, uint type, uint time, uint size, bool converted);

    [LoggerMessage(EventId = 1623, Level = LogLevel.Debug,
        Message = "[AccountData] type {Type}: saved to server (time {Time}, {Size} bytes)")]
    public static partial void SavedToServer(ILogger logger, uint type, long time, uint size);

    [LoggerMessage(EventId = 1624, Level = LogLevel.Warning,
        Message = "[AccountData] type {Type}: {Size} bytes is over the legacy server's 65535 byte limit, kept locally only")]
    public static partial void TooLargeForServer(ILogger logger, uint type, uint size);
}
