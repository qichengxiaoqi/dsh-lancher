namespace DshPlusPlus.Core.Models;

public sealed record UpdateOperationResult(
    bool Succeeded,
    UpdateState State,
    string Stage,
    string Message,
    ProcessResult? ProcessResult = null,
    RepositorySnapshot? Snapshot = null);
