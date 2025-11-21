using Persistasaurus.Features.Execution;

namespace Persistasaurus.Data;

/// <summary>
/// Entity representing an execution log entry in the database.
/// </summary>
public class ExecutionLogEntry
{
    /// <summary>
    /// Gets or sets the flow identifier.
    /// </summary>
    public string FlowId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the step sequence number within the flow.
    /// </summary>
    public int Step { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this invocation started (Unix milliseconds).
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the class name containing the flow/step method.
    /// </summary>
    public string ClassName { get; set; } = null!;

    /// <summary>
    /// Gets or sets the method name of the flow/step.
    /// </summary>
    public string MethodName { get; set; } = null!;

    /// <summary>
    /// Gets or sets the delay in milliseconds for delayed steps (nullable).
    /// </summary>
    public long? Delay { get; set; }

    /// <summary>
    /// Gets or sets the status of this invocation.
    /// </summary>
    public InvocationStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the number of attempts made to execute this step.
    /// </summary>
    public int Attempts { get; set; } = 1;

    /// <summary>
    /// Gets or sets the serialized parameters (JSON).
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the serialized return value (JSON).
    /// </summary>
    public string? ReturnValue { get; set; }
}
