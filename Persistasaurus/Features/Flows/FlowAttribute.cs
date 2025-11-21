namespace Persistasaurus.Features.Flows;

/// <summary>
/// Marks a method as a durable execution flow entry point.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class FlowAttribute : Attribute
{
}
