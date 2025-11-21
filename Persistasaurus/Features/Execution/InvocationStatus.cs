namespace Persistasaurus.Features.Execution;

/// <summary>
/// Represents the status of a flow or step invocation.
/// </summary>
public enum InvocationStatus
{
    /// <summary>
    /// The invocation has started but not yet completed.
    /// </summary>
    Pending,

    /// <summary>
    /// The invocation is waiting for an external signal to continue.
    /// </summary>
    WaitingForSignal,

    /// <summary>
    /// The invocation has completed successfully.
    /// </summary>
    Complete
}
