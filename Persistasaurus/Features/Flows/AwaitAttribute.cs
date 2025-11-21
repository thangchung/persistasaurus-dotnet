namespace Persistasaurus.Features.Flows;

/// <summary>
/// Marks a step method that should pause execution and wait for an external signal (human-in-the-loop).
/// When a step is marked with [Await], the flow execution will pause at this step until Resume() is called.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AwaitAttribute : Attribute
{
}
