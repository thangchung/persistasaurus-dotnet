namespace Persistasaurus.Features.Interception;

/// <summary>
/// Represents the type of call being made to a flow or step.
/// </summary>
internal enum CallType
{
    /// <summary>
    /// Normal execution of a flow or step.
    /// </summary>
    Run,

    /// <summary>
    /// Awaiting an external signal before continuing.
    /// </summary>
    Await,

    /// <summary>
    /// Resuming a flow after receiving an external signal.
    /// </summary>
    Resume
}
