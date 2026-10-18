using System.Text.Json;
// Test-only server. This executable never connects to OpenAI or reads credentials.
var mode = args.FirstOrDefault() ?? "ok";
var reads = 0;
while (await Console.In.ReadLineAsync() is { } line)
{
    using var request = JsonDocument.Parse(line); var root = request.RootElement;
    if (!root.TryGetProperty("id", out var id)) continue;
    var method = root.GetProperty("method").GetString();
    if (method == "account/rateLimits/read" && mode == "slow") await Task.Delay(Timeout.Infinite);
    if (method == "account/rateLimits/read" && mode is "rpc" or "incompatible")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt32(), error = new { code = mode == "rpc" ? -32603 : -32601, message = "PRIVATE_ERROR_TOKEN" } })); continue;
    }
    if (method == "account/rateLimits/read" && mode is "malformed" or "missing-result")
    {
        Console.WriteLine(mode == "malformed" ? "[]" : JsonSerializer.Serialize(new { id = id.GetInt32() })); continue;
    }
    if (method == "account/rateLimits/read" && mode == "invalid-json") { Console.WriteLine("{PRIVATE_ERROR_TOKEN"); continue; }
    object result = method switch
    {
        "initialize" => new { userAgent = "test-only" },
        "account/read" => mode == "login" ? new { account = (object?)null } : mode == "apikey" ? new { account = (object?)new { type = "apiKey" } } :
            new { account = (object?)new { type = "chatgpt", email = mode == "switch" && ++reads > 1 ? "changed@example.com" : "sample@example.com" } },
        _ => new { accountId = "sample-quota-account", rateLimits = new { limitId = "codex", primary = new { usedPercent = 7, windowDurationMins = 10080, resetsAt = 1900000000 } } }
    };
    Console.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt32(), result }));
}




