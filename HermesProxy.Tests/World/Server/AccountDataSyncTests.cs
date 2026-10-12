using HermesProxy.World.Server;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// Which account data types are shared with the legacy server, and so with a native 3.3.5a
/// client playing the same account.
/// </summary>
/// <remarks>
/// The 3.3.5a client's config cache replaced the modern one through the server: logging in with
/// it reset the modern client's camera distance and status text (2026-10-11, 20:05 and 20:08).
/// </remarks>
public class AccountDataSyncTests
{
    [Theory]
    [InlineData(0u)] // global config
    [InlineData(1u)] // per-character config
    [InlineData(6u)] // layout
    [InlineData(7u)] // chat
    public void ClientSpecificTypes_StayWithTheProxy(uint type)
    {
        Assert.False(AccountDataManager.IsSyncedWithServer(type));
    }

    [Theory]
    [InlineData(2u)] // global bindings
    [InlineData(3u)] // per-character bindings
    [InlineData(4u)] // global macros
    [InlineData(5u)] // per-character macros
    public void BindingsAndMacros_AreSharedWithTheServer(uint type)
    {
        Assert.True(AccountDataManager.IsSyncedWithServer(type));
    }

    [Fact]
    public void NewerServerConfig_DoesNotReplaceTheProxysCopy()
    {
        var accountData = new AccountDataManager("TEST", "Realm");
        accountData.ServerTimes[0] = 1791767110;
        accountData.ServerTimes[2] = 1791767110;

        Assert.False(accountData.ServerIsNewer(0));
        Assert.Equal(0, accountData.GetEffectiveTime(0));
        Assert.True(accountData.ServerIsNewer(2));
    }
}
