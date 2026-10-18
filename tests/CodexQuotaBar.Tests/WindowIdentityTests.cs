using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class WindowIdentityTests
{
    [Theory]
    [InlineData("OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0", "Chrome_WidgetWin_1", 2560, 1390, true)]
    [InlineData("OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0", "#32770", 800, 600, false)]
    [InlineData("", "ConsoleWindowClass", 800, 600, false)]
    [InlineData("Other.App_1", "Chrome_WidgetWin_1", 800, 600, false)]
    [InlineData("OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0", "Chrome_WidgetWin_1", 60, 32, false)]
    public void OnlyRecognizedClientSurfacesAreEligible(string package, string windowClass, int width, int height, bool expected)
        => Assert.Equal(expected, WindowIdentityPolicy.IsCodexSurface(package, windowClass, width, height));
}

