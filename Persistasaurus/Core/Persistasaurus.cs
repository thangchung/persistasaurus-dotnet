using System.Reflection;
using Microsoft.Extensions.Logging;
using Persistasaurus.Features.Execution;
using Persistasaurus.Features.Flows;
using Persistasaurus.Features.Interception;

namespace Persistasaurus.Core;

/// <summary>
/// Main entry point for creating and managing durable execution flows.
/// </summary>
public static class Persistasaurus
{
    private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole())
        .CreateLogger(typeof(Persistasaurus));

    static Persistasaurus()
    {
        // Recover incomplete flows on startup
        _ = RecoverIncompleteFlowsAsync();
    }

    /// <summary>
    /// Gets a flow instance for the specified interface type and ID.
    /// </summary>
    /// <typeparam name="T">The flow interface type (must be an interface).</typeparam>
    /// <param name="flowId">The unique identifier for this flow execution.</param>
    /// <returns>A flow instance that can be used to run the flow.</returns>
    public static FlowInstance<T> GetFlow<T>(Guid flowId) where T : class
    {
        if (!typeof(T).IsInterface)
        {
            throw new ArgumentException(
                $"Type {typeof(T).Name} must be an interface. DispatchProxy requires interface-based flows.",
                nameof(T));
        }

        // Create an instance of the concrete implementation
        var concreteType = FindConcreteImplementation<T>();
        if (concreteType == null)
        {
            throw new InvalidOperationException(
                $"No concrete implementation found for interface {typeof(T).Name}. " +
                "Ensure there is a class implementing this interface in the same assembly.");
        }

        var target = Activator.CreateInstance(concreteType) as T;
        if (target == null)
        {
            throw new InvalidOperationException(
                $"Failed to create instance of {concreteType.Name}");
        }

        // Create proxy
        var proxy = FlowInterceptor<T>.Create(target, flowId);

        return new FlowInstance<T>(flowId, proxy);
    }

    /// <summary>
    /// Awaits an external signal before continuing the flow.
    /// Used for "human in the loop" scenarios.
    /// </summary>
    /// <param name="action">The action that represents the waiting step.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task AwaitAsync(Func<Task> action)
    {
        FlowInterceptor<object>.SetCallType(CallType.Await);
        await action();
    }

    /// <summary>
    /// Recovers and reschedules incomplete flows found in the execution log.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task RecoverIncompleteFlowsAsync()
    {
        try
        {
            var executionLog = ExecutionLog.Instance;
            var incompleteFlows = await executionLog.GetIncompleteFlowsAsync();

            if (incompleteFlows.Count > 0)
            {
                _logger.LogInformation(
                    "Found {Count} incomplete flows, scheduling for execution",
                    incompleteFlows.Count);

                foreach (var flow in incompleteFlows)
                {
                    _logger.LogInformation(
                        "Recovering incomplete flow {FlowId} for {ClassName}.{MethodName} (attempt {Attempts})",
                        flow.FlowId, flow.ClassName, flow.MethodName, flow.Attempts);

                    // Schedule for async execution
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await RunFlowAsync(flow.FlowId, flow.ClassName, flow.MethodName, flow.Parameters);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Failed to recover flow {FlowId} for {ClassName}.{MethodName}",
                                flow.FlowId, flow.ClassName, flow.MethodName);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover incomplete flows");
        }
    }

    private static async Task RunFlowAsync(Guid flowId, string className, string methodName, object?[]? parameters)
    {
        _logger.LogInformation("Running flow {FlowId} for {ClassName}.{MethodName}",
            flowId, className, methodName);

        try
        {
            // Find the flow type by class name
            var flowType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == className || t.FullName == className);

            if (flowType == null)
            {
                throw new InvalidOperationException($"Flow type {className} not found");
            }

            // Find the interface it implements
            var flowInterface = flowType.GetInterfaces()
                .FirstOrDefault(i => i.GetMethod(methodName) != null);

            if (flowInterface == null)
            {
                throw new InvalidOperationException(
                    $"No interface found on {className} with method {methodName}");
            }

            // Use reflection to call GetFlow<T>
            var getFlowMethod = typeof(Persistasaurus)
                .GetMethod(nameof(GetFlow), BindingFlags.Public | BindingFlags.Static)
                ?.MakeGenericMethod(flowInterface);

            if (getFlowMethod == null)
            {
                throw new InvalidOperationException("GetFlow method not found");
            }

            var flowInstance = getFlowMethod.Invoke(null, new object[] { flowId });
            if (flowInstance == null)
            {
                throw new InvalidOperationException("Failed to create flow instance");
            }

            // Get the RunAsync method
            var runAsyncMethod = flowInstance.GetType()
                .GetMethod(nameof(FlowInstance<object>.RunAsync));

            if (runAsyncMethod == null)
            {
                throw new InvalidOperationException("RunAsync method not found");
            }

            // Create an action that invokes the flow method
            var flowMethod = flowInterface.GetMethod(methodName);
            if (flowMethod == null)
            {
                throw new InvalidOperationException($"Method {methodName} not found");
            }

            Action<object> action = (flow) =>
            {
                flowMethod.Invoke(flow, parameters);
            };

            // Invoke RunAsync
            var task = runAsyncMethod.Invoke(flowInstance, new object[] { action }) as Task;
            if (task != null)
            {
                await task;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to execute flow {FlowId} for {ClassName}.{MethodName}",
                flowId, className, methodName);
        }
    }

    private static Type? FindConcreteImplementation<T>() where T : class
    {
        var interfaceType = typeof(T);

        // Search in all loaded assemblies
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch
                {
                    return Array.Empty<Type>();
                }
            })
            .FirstOrDefault(t =>
                t.IsClass &&
                !t.IsAbstract &&
                interfaceType.IsAssignableFrom(t));
    }
}
