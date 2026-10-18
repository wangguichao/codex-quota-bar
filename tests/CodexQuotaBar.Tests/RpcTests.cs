using System.Text.Json;
using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class RpcTests
{
    [Fact]
    public async Task IgnoresNotificationsAndRejectsUnsolicitedServerRequests()
    {
        var input = new StringReader("""{"method":"notice"}""" + "\n" +
            """{"id":900,"method":"dangerous/unknown"}""" + "\n" +
            """{"id":1,"result":{"ok":true}}""" + "\n");
        var output = new StringWriter();
        var rpc = new RpcConnection(input, output);
        var result = await rpc.RequestAsync("account/read", new { refreshToken = false }, CancellationToken.None);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Contains("-32601", output.ToString());
        Assert.DoesNotContain("dangerous/unknown", output.ToString());
    }
    [Fact]
    public async Task RawServerErrorDoesNotLeakThroughException()
    {
        var rpc = new RpcConnection(new StringReader("""{"id":1,"error":{"code":-32000,"message":"SECRET_TOKEN secret@example.com"}}""" + "\n"), new StringWriter());
        var error = await Assert.ThrowsAsync<RpcFailureException>(() => rpc.RequestAsync("account/read", null, CancellationToken.None));
        Assert.Equal(-32000, error.Code);
        Assert.DoesNotContain("SECRET_TOKEN", error.ToString());
        Assert.DoesNotContain("secret@example.com", error.ToString());
    }
    [Fact]
    public async Task EofAndMalformedRepliesFailClearly()
    {
        await Assert.ThrowsAsync<EndOfStreamException>(() => new RpcConnection(new StringReader(""), new StringWriter()).RequestAsync("initialize", null, CancellationToken.None));
        await Assert.ThrowsAnyAsync<JsonException>(() => new RpcConnection(new StringReader("broken\n"), new StringWriter()).RequestAsync("initialize", null, CancellationToken.None));
    }
    [Fact]
    public async Task NotificationsHaveNoRequestId()
    {
        var output = new StringWriter();
        await new RpcConnection(new StringReader(""), output).NotifyAsync("initialized", CancellationToken.None);
        using var payload = JsonDocument.Parse(output.ToString());
        Assert.False(payload.RootElement.TryGetProperty("id", out _));
    }
}


