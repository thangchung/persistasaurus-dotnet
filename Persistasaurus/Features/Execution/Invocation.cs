namespace Persistasaurus.Features.Execution;

/// <summary>
/// Represents a recorded invocation of a flow or step method.
/// </summary>
public record Invocation(
    Guid FlowId,
    int Step,
    DateTimeOffset Timestamp,
    string ClassName,
    string MethodName,
    InvocationStatus Status,
    int Attempts,
    object[]? Parameters,
    object? ReturnValue)
{
    /// <summary>
    /// Gets whether this invocation represents the flow entry point (step 0).
    /// </summary>
    public bool IsFlow => Step == 0;
}
