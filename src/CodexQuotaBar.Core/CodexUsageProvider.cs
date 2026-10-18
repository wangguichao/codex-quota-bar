using System.Diagnostics;
using System.Text.Json;

namespace CodexQuotaBar.Core;

/// <summary>Each read owns exactly one short-lived app-server process. No credential files are opened.</summary>
public sealed class CodexUsageProvider(string executable, IReadOnlyList<string>? arguments = null) : IUsageProvider
{
    public async Task<UsageSnapshot> ReadAsync(CancellationToken token) => (await InspectAsync(token).ConfigureAwait(false)).Snapshot;
    public async Task<AccountInspection> InspectAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new UsageFailureException(UsageState.Incompatible);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments ?? ["app-server", "--stdio"]) start.ArgumentList.Add(argument);
        using var child = new Process { StartInfo = start };
        using var drainStop = new CancellationTokenSource();
        var started = false; var exited = false;
        var drain = Task.CompletedTask;
        UsageSnapshot? snapshot = null;
        var email = "";
        try
        {
            started = child.Start();
            drain = DrainAsync(child.StandardError, drainStop.Token);
            var rpc = new RpcConnection(child.StandardOutput, child.StandardInput);
            await rpc.RequestAsync("initialize", new { clientInfo = new { name = "codex_quota_bar", title = "Codex Quota Bar", version = "0.2.0" }, capabilities = (object?)null }, token).ConfigureAwait(false);
            await rpc.NotifyAsync("initialized", token).ConfigureAwait(false);
            var before = await rpc.RequestAsync("account/read", new { refreshToken = false }, token).ConfigureAwait(false);
            var account = QuotaParser.Property(before, "account");
            if (account.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) throw new UsageFailureException(UsageState.NeedsLogin);
            if (QuotaParser.Text(account, "type") != "chatgpt") throw new UsageFailureException(UsageState.Incompatible);
            var usage = await rpc.RequestAsync("account/rateLimits/read", null, token).ConfigureAwait(false);
            var after = await rpc.RequestAsync("account/read", new { refreshToken = false }, token).ConfigureAwait(false);
            snapshot = QuotaParser.Parse(before, usage, after, DateTimeOffset.UtcNow);
            email = QuotaParser.Text(account, "email") ?? "";
        }
        catch (RpcFailureException error) { throw new UsageFailureException(error.Code == -32601 ? UsageState.Incompatible : UsageState.ReadError); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or JsonException) { throw new UsageFailureException(UsageState.Incompatible); }
        finally
        {
            if (started)
            {
                try { child.StandardInput.Close(); } catch (IOException) { /* Child may already have closed its pipe. */ }
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await child.WaitForExitAsync(shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* Exit raced cancellation. */ }
                    await child.WaitForExitAsync().ConfigureAwait(false);
                }
                exited = child.HasExited;
            }
            drainStop.Cancel();
            try { await drain.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        return new(snapshot! with { SourceProcessExited = exited }, email);
    }
    private static async Task DrainAsync(TextReader reader, CancellationToken token)
    {
        var buffer = new char[2048];
        try { while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) != 0) { } }
        catch (IOException) { /* Stderr is deliberately discarded; stdout determines the read result. */ }
    }
}




