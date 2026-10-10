using System;
using System.Globalization;

namespace HermesProxy.World.Client;

/// <summary>
/// Rewrites the encoded subject and body of an auction-house mail from the 3.3.5a form to the
/// form a 3.4.3 client decodes.
/// </summary>
/// <remarks>
/// The client builds an auction mail's subject ("Auction successful: ...") and its invoice (buyer,
/// bid, deposit, cut) from these strings, so they are data, not text. The two sides agree on the
/// mail type numbers (0 outbid, 1 won, 2 sold, 3 expired, 4 removed, 5 cancelled, 6 sale pending /
/// invoice) and differ in layout:
/// <code>
///                3.3.5a (AzerothCore)                         3.4 (TrinityCore wotlk_classic)
/// subject        item:0:type:auctionId:count                  item:0:type:auctionId:count:pet:context:0:0:0:0:bonusCount:bonuses
/// body           guid(hex, 16 wide):bid:buyout:deposit:       outbid/expired/removed/cancelled: empty
///                cut:moneyDelay:eta, for every type           won: guid:bid:buyout:0
///                                                             sold: guid:bid:buyout:deposit:cut:0
///                                                             invoice: guid:bid:buyout:deposit:cut:moneyDelay:eta:0
/// guid           hex raw value, space padded                  "Player-realm-COUNTER" (8-digit upper-case hex)
/// </code>
/// A string not in the 3.3.5a layout is left as it is.
/// </remarks>
public static class AuctionMailText
{
    /// <summary>The 3.4 subject, and the mail type it carries; null type when not an auction subject.</summary>
    public static (string Subject, int? Type) Subject(string legacy)
    {
        string[] parts = legacy.Split(':');
        if (parts.Length != 5 || !AllNumbers(parts) || !int.TryParse(parts[2], out int type))
            return (legacy, null);

        // No battle pet, item context or bonus lists on a 3.3.5a item: all 0, and an empty list.
        return ($"{legacy}:0:0:0:0:0:0:0:", type);
    }

    /// <summary>The 3.4 body of an auction mail of <paramref name="type"/>.</summary>
    /// <param name="playerGuid">The 3.4 guid of a player from the raw legacy guid value.</param>
    public static string Body(string legacy, int type, Func<ulong, WowGuid128> playerGuid)
    {
        if (type is 0 or 3 or 4 or 5)
            return "";

        string[] parts = legacy.Split(':');
        if (parts.Length != 7 || !ulong.TryParse(parts[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong rawGuid) ||
            !AllNumbers(parts.AsSpan(1)))
            return legacy;

        string guid = rawGuid == 0 ? new string('0', 16) : FormatPlayer(playerGuid(rawGuid));
        string bid = parts[1], buyout = parts[2], deposit = parts[3], cut = parts[4], delay = parts[5], eta = parts[6];
        return type switch
        {
            1 => $"{guid}:{bid}:{buyout}:0",
            2 => $"{guid}:{bid}:{buyout}:{deposit}:{cut}:0",
            6 => $"{guid}:{bid}:{buyout}:{deposit}:{cut}:{delay}:{eta}:0",
            _ => legacy,
        };
    }

    /// <summary>A player guid as TrinityCore's ObjectGuid::ToString writes it.</summary>
    public static string FormatPlayer(WowGuid128 guid)
        => $"Player-{guid.GetRealmId()}-{guid.Low:X8}";

    private static bool AllNumbers(ReadOnlySpan<string> parts)
    {
        foreach (string part in parts)
        {
            if (part.Length == 0 || !ulong.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                return false;
        }
        return true;
    }
}
