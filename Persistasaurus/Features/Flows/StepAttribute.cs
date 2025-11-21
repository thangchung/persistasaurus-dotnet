namespace Persistasaurus.Features.Flows;

/// <summary>
/// Marks a method as a durable execution step that will be persisted.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class StepAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the delay before executing this step.
    /// Use long.MinValue (default) for no delay.
    /// </summary>
    public long Delay { get; set; } = long.MinValue;

    /// <summary>
    /// Gets or sets the time unit for the delay.
    /// </summary>
    public TimeUnit TimeUnit { get; set; } = TimeUnit.Seconds;
}

/// <summary>
/// Time units for step delays.
/// </summary>
public enum TimeUnit
{
    Seconds,
    Minutes,
    Hours,
    Days
}
