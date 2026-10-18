using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class PreviewStatusTests
{
    [Theory]
    [InlineData(false, true, true, true, PreviewStatus.Stopped)]
    [InlineData(true, false, false, false, PreviewStatus.WaitingForTarget)]
    [InlineData(true, true, false, true, PreviewStatus.WaitingForForeground)]
    [InlineData(true, true, true, false, PreviewStatus.Rendering)]
    [InlineData(true, true, true, true, PreviewStatus.Showing)]
    [InlineData(false, false, false, false, PreviewStatus.Stopped)]
    public void DisplayStatusRequiresBothNativeVisibilityAndRenderedContent(bool running, bool targetFound, bool nativeVisible, bool rendered, PreviewStatus expected)
        => Assert.Equal(expected, PreviewStatusPolicy.Evaluate(running, targetFound, nativeVisible, rendered));
}

