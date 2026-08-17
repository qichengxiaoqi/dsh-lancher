using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public interface IDeepSeekApiClient
{
    Task<ApiConnectionResult> TestConnectionAsync(string apiKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeepSeekModelInfo>> GetModelsAsync(string apiKey, CancellationToken cancellationToken);
    Task<DeepSeekBalanceSnapshot> GetBalanceAsync(string apiKey, CancellationToken cancellationToken);
}

public sealed class DeepSeekApiClient : IDeepSeekApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly bool _ownsClient;

    public DeepSeekApiClient(
        HttpMessageHandler? handler = null,
        string baseUrl = "https://api.deepseek.com")
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _ownsClient = true;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public async Task<ApiConnectionResult> TestConnectionAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ApiConnectionResult(false, null, 0, "API Key 未设置", DateTimeOffset.UtcNow);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await SendAsync("models", apiKey, cancellationToken);
            stopwatch.Stop();
            var message = response.IsSuccessStatusCode
                ? "DeepSeek API 连接正常"
                : $"DeepSeek API 返回 HTTP {(int)response.StatusCode}";
            return new ApiConnectionResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                message,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ApiConnectionResult(false, null, stopwatch.ElapsedMilliseconds, "请求超时", DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return new ApiConnectionResult(false, null, stopwatch.ElapsedMilliseconds, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    public async Task<IReadOnlyList<DeepSeekModelInfo>> GetModelsAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync("models", apiKey, cancellationToken);
        await EnsureSuccessAsync(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("模型列表响应缺少 data 数组。");

        return data.EnumerateArray()
            .Select(item => new DeepSeekModelInfo(
                GetString(item, "id"),
                GetString(item, "object"),
                GetString(item, "owned_by")))
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .ToArray();
    }

    public async Task<DeepSeekBalanceSnapshot> GetBalanceAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        var retrievedAt = DateTimeOffset.UtcNow;
        using var response = await SendAsync("user/balance", apiKey, cancellationToken);
        var statusCode = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            return new DeepSeekBalanceSnapshot(
                false,
                [],
                statusCode,
                $"余额查询返回 HTTP {statusCode}",
                retrievedAt);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var isAvailable = root.TryGetProperty("is_available", out var available)
                          && available.ValueKind == JsonValueKind.True;
        var balances = root.TryGetProperty("balance_infos", out var infos)
            && infos.ValueKind == JsonValueKind.Array
            ? infos.EnumerateArray().Select(info => new DeepSeekBalanceInfo(
                GetString(info, "currency"),
                GetDecimal(info, "total_balance"),
                GetDecimal(info, "granted_balance"),
                GetDecimal(info, "topped_up_balance"))).ToArray()
            : [];

        return new DeepSeekBalanceSnapshot(
            isAvailable,
            balances,
            statusCode,
            isAvailable ? "余额查询成功" : "当前账户不可用",
            retrievedAt);
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(
        string relativePath,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"DeepSeek API 返回 HTTP {(int)response.StatusCode}: {Truncate(detail, 240)}");
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static decimal GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0m;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            return number;
        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), out var textNumber))
            return textNumber;
        return 0m;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
