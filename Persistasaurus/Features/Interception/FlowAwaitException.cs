namespace Persistasaurus.Features.Interception;

/// <summary>
/// Exception thrown when a flow reaches an [Await] step and needs to pause execution.
/// This is caught internally and signals that the flow should wait for external input.
/// </summary>
internal class FlowAwaitException : Exception
{
    public FlowAwaitException(string message) : base(message)
    {
    }
}
