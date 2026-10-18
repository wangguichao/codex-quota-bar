using System.Diagnostics;
using System.Text.Json;
using CodexQuotaBar.Core;

if (args.Length != 2 || !Path.IsPathFullyQualified(args[0]) || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: CodexQuotaBar.Probe <absolute-codex.exe> <sanitized-output.json>");
    return 2;
}
var binary = Path.GetFullPath(args[0]);
var info = new ProcessStartInfo(binary)
{
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
info.ArgumentList.Add("app-server");
info.ArgumentList.Add("--stdio");
using var child = new Process { StartInfo = info };
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
object evidence;
var exit = 1;
var errorDrain = Task.CompletedTask;
var cleaned = false;
var started = false;
try
{
    started = child.Start();
    // Drain and discard stderr: authentication and config diagnostics must not be saved.
    errorDrain = Task.Run(async () =>
    {
        var buffer = new char[2048];
        while (await child.StandardError.ReadAsync(buffer.AsMemory(), lifetime.Token) != 0) { }
    });
    var rpc = new RpcConnection(child.StandardOutput, child.StandardInput);
    await rpc.RequestAsync("initialize", new
    {
        clientInfo = new { name = "codex_quota_bar_probe", title = "Quota feasibility probe", version = "0.1.0" },
        capabilities = (object?)null
    }, lifetime.Token);
    await rpc.NotifyAsync("initialized", lifetime.Token);
    var account = await rpc.RequestAsync("account/read", new { refreshToken = false }, lifetime.Token);
    var usage = await rpc.RequestAsync("account/rateLimits/read", null, lifetime.Token);
    evidence = new { status = "queried", sourceBinary = binary, data = ProbeEvidence.Sanitize(account, usage) };
    exit = 0;
}
catch (Exception error) when (error is RpcFailureException or OperationCanceledException or IOException
    or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
{
    // Never serialize Exception.Message, stack traces, raw stderr or full auth responses.
    evidence = new
    {
        status = "unavailable",
        sourceBinary = binary,
        errorType = error.GetType().Name,
        rpcCode = error is RpcFailureException rpc ? (int?)rpc.Code : null,
        desktopIdentityVerified = false
    };
}
finally
{
    if (started)
    {
        child.StandardInput.Close();
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await child.WaitForExitAsync(shutdown.Token); }
        catch (OperationCanceledException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        cleaned = child.HasExited;
    }
    lifetime.Cancel();
    try { await errorDrain; } catch (OperationCanceledException) { }
}
var report = JsonSerializer.Serialize(new { evidence, ownedChildExited = cleaned }, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
await File.WriteAllTextAsync(args[1], report);
Console.WriteLine(report);
return exit;


