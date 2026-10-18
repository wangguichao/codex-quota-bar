using System.Text.Json;

namespace CodexQuotaBar.Core;

public sealed class RpcFailureException(int code) : Exception($"Codex RPC error ({code}).")
{
    public int Code { get; } = code;
}

/// <summary>One caller at a time. The caller owns timeout, transport and process lifetime.</summary>
public sealed class RpcConnection(TextReader input, TextWriter output)
{
    private int nextId;

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = ++nextId;
        await WriteAsync(new { id, method, @params = parameters }, cancellationToken);
        while (true)
        {
            var line = await input.ReadLineAsync(cancellationToken) ?? throw new EndOfStreamException("Codex transport closed.");
            if (line.Length > 1_048_576)
                throw new InvalidDataException("Codex response exceeds the probe limit.");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid Codex RPC envelope.");
            if (root.TryGetProperty("method", out _) && root.TryGetProperty("id", out var serverId))
            {
                await WriteAsync(new { id = serverId, error = new { code = -32601, message = "Unsupported request" } }, cancellationToken);
                continue;
            }
            if (!root.TryGetProperty("id", out var replyId) || !replyId.TryGetInt32(out var replyNumber) || replyNumber != id)
                continue;
            if (root.TryGetProperty("error", out var error))
            {
                var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var rawCode)
                    && rawCode.TryGetInt32(out var number) ? number : -32000;
                throw new RpcFailureException(code);
            }
            if (!root.TryGetProperty("result", out var result))
                throw new InvalidDataException("Codex reply has no result.");
            return result.Clone();
        }
    }

    public Task NotifyAsync(string method, CancellationToken cancellationToken)
        => WriteAsync(new { method, @params = new { } }, cancellationToken);

    private async Task WriteAsync<T>(T value, CancellationToken cancellationToken)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}

