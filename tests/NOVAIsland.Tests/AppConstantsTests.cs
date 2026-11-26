using NOVAIsland.Core.Configuration;
using Xunit;

namespace NOVAIsland.Tests;

public sealed class AppConstantsTests
{
    [Fact]
    public void Defaults_ShouldReflectDoomIslandBranding()
    {
        Assert.Equal("DOOM Island", AppConstants.DefaultAppName);
        Assert.Equal("DOOM", AppConstants.DefaultShortName);
        Assert.Equal("com.doomisland.host", AppConstants.NativeMessagingHostName);
    }
}

