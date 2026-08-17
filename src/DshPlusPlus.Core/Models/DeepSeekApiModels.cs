namespace DshPlusPlus.Core.Models;

public sealed record ApiConnectionResult(
    bool Success,
    int? StatusCode,
    long LatencyMs,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record DeepSeekModelInfo(
    string Id,
    string Object,
    string OwnedBy);

public sealed record DeepSeekBalanceInfo(
    string Currency,
    decimal TotalBalance,
    decimal GrantedBalance,
    decimal ToppedUpBalance);

public sealed record DeepSeekBalanceSnapshot(
    bool IsAvailable,
    IReadOnlyList<DeepSeekBalanceInfo> Balances,
    int? StatusCode,
    string Message,
    DateTimeOffset RetrievedAt);

public sealed record CredentialStatus(
    bool HasFileValue,
    bool HasEnvironmentOverride,
    string MaskedValue,
    bool CanWrite,
    string Message);
