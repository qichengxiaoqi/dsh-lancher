using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DshPlusPlus.Core.Models;

namespace DshPlusPlus.Core.Services;

public static class ServiceStatusMapper
{
    public static ServiceProbeResult TcpUnavailable() =>
        new(ServiceState.Stopped, "3080 端口未监听");

    public static ServiceProbeResult FromHttpStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 200 and <= 599
            ? new ServiceProbeResult(ServiceState.Running, $"Web 服务响应 HTTP {code}")
            : new ServiceProbeResult(ServiceState.StartFailed, $"Web 服务响应 HTTP {code}");
    }

    public static ServiceProbeResult HttpFailure(string detail) =>
        new(ServiceState.StartFailed, $"端口已监听，但 Web 请求失败：{detail}");

    public static ServiceProbeResult UnknownFailure(string detail) =>
        new(ServiceState.Unknown, $"状态探测失败：{detail}");
}

public sealed class ServiceStatusProbe
{
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(2);

    private readonly DshPaths _paths;
    private readonly HttpClient _httpClient;
    private readonly ServiceProbeCoordinator _dshProbeCoordinator;

    public ServiceStatusProbe(DshPaths paths, HttpClient? httpClient = null)
    {
        _paths = paths;
        _httpClient = httpClient ?? new HttpClient();
        _dshProbeCoordinator = new ServiceProbeCoordinator(ProbeDshCoreAsync);
    }

    public async Task<ServiceProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, _paths.Port)
                .WaitAsync(TcpTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceStatusMapper.TcpUnavailable();
        }
        catch (SocketException)
        {
            return ServiceStatusMapper.TcpUnavailable();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceStatusMapper.UnknownFailure(ex.Message);
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                    _paths.WebUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .WaitAsync(HttpTimeout, cancellationToken);
            return ServiceStatusMapper.FromHttpStatus(response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceStatusMapper.HttpFailure("请求超时");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceStatusMapper.HttpFailure(ex.Message);
        }
    }

    public Task<ServiceProbeResult> ProbeDshAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false) =>
        _dshProbeCoordinator.ProbeAsync(cancellationToken, forceRefresh);

    private async Task<ServiceProbeResult> ProbeDshCoreAsync(CancellationToken cancellationToken)
    {
        var basic = await ProbeAsync(cancellationToken);
        if (basic.State != ServiceState.Running)
            return basic;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _paths.WebUrl.TrimEnd('/') + "/api/host.describe")
            {
                Content = new StringContent(
                    "{\"type\":\"client-request\",\"rpcId\":\"dsh-plus-plus-health\",\"method\":\"host.describe\",\"payload\":{}}",
                    Encoding.UTF8,
                    "application/json")
            };
            using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .WaitAsync(HttpTimeout, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode || !IsSuccessfulHostDescribe(body))
                return ServiceStatusMapper.HttpFailure("DSH API health check returned an invalid response");

            return new ServiceProbeResult(ServiceState.Running, "DSH API health check passed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceStatusMapper.HttpFailure("DSH API health check timed out");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceStatusMapper.HttpFailure(ex.Message);
        }
    }

    private static bool IsSuccessfulHostDescribe(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return root.TryGetProperty("type", out var type)
                   && type.GetString() == "server-response"
                   && root.TryGetProperty("result", out var result)
                   && result.TryGetProperty("ok", out var ok)
                   && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
