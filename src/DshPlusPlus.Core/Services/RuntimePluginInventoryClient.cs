using System.Text.Json;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public sealed class RuntimePluginInventoryClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public RuntimePluginInventoryClient(HttpMessageHandler? handler = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _ownsClient = true;
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
    }

    public async Task<IReadOnlyList<RuntimePluginEntry>> ListAsync(
        string webUrl,
        CancellationToken cancellationToken)
    {
        var baseUri = new Uri(webUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var endpoint = new Uri(baseUri, "api/pluginInventory/list");
        var rpcId = Guid.NewGuid().ToString("N");
        var body = JsonSerializer.Serialize(new
        {
            type = "client-request",
            rpcId,
            method = "pluginInventory/list",
            payload = new { args = new { } }
        });
        using var response = await _httpClient.PostAsync(
            endpoint,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var value = document.RootElement.GetProperty("result").GetProperty("value");
        var entries = value.GetProperty("entries");
        return entries.EnumerateArray().Select(entry => new RuntimePluginEntry(
            entry.GetProperty("entryId").GetString() ?? string.Empty,
            entry.GetProperty("moduleName").GetString() ?? string.Empty,
            entry.GetProperty("enabled").GetBoolean(),
            entry.TryGetProperty("fiberPhase", out var phase) && phase.ValueKind != JsonValueKind.Null
                ? phase.GetString()
                : null)).ToArray();
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
