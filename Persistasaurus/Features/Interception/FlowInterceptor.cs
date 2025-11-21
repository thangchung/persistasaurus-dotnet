using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Persistasaurus.Features.Execution;
using Persistasaurus.Features.Flows;

namespace Persistasaurus.Features.Interception;

/// <summary>
/// Interceptor for flow methods using DispatchProxy to transparently log and replay executions.
/// </summary>
/// <typeparam name="T">The flow interface type.</typeparam>
internal class FlowInterceptor<T> : DispatchProxy where T : class
{
    private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole())
        .CreateLogger<FlowInterceptor<T>>();

    private static readonly ActivitySource _activitySource = new("Persistasaurus.FlowInterceptor", "1.0.0");

    private T? _target;
    private Guid _flowId;
    private int _currentStep;
    private ExecutionLog? _executionLog;
    private static readonly AsyncLocal<CallType> _callType = new();
    private static readonly ConcurrentDictionary<Guid, WaitCondition> _waitConditions = new();

    public static T Create(T target, Guid flowId)
    {
        var proxy = Create<T, FlowInterceptor<T>>() as FlowInterceptor<T>;
        if (proxy == null)
        {
            throw new InvalidOperationException("Failed to create proxy");
        }

        proxy._target = target;
        proxy._flowId = flowId;
        proxy._currentStep = 0;
        proxy._executionLog = ExecutionLog.Instance;

        return (proxy as T)!;
    }

    public static void SetCallType(CallType callType)
    {
        _callType.Value = callType;
    }

    public static CallType GetCallType()
    {
        return _callType.Value;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null || _target == null || _executionLog == null)
        {
            return null;
        }

        // Check if method requires logging (has Flow or Step attribute)
        var isFlow = targetMethod.GetCustomAttribute<FlowAttribute>() != null;
        var stepAttr = targetMethod.GetCustomAttribute<StepAttribute>();
        var isStep = stepAttr != null;

        if (!isFlow && !isStep)
        {
            // Not a flow or step, just invoke normally
            return targetMethod.Invoke(_target, args);
        }

        return InvokeAsync(targetMethod, args).GetAwaiter().GetResult();
    }

    private async Task<object?> InvokeAsync(MethodInfo targetMethod, object?[]? args)
    {
        var className = targetMethod.DeclaringType?.Name ?? "Unknown";
        var methodName = targetMethod.Name;
        var isFlow = targetMethod.GetCustomAttribute<FlowAttribute>() != null;
        var stepAttr = targetMethod.GetCustomAttribute<StepAttribute>();
        var awaitAttr = targetMethod.GetCustomAttribute<AwaitAttribute>();
        var isAwaitStep = awaitAttr != null;
        var delay = GetDelay(stepAttr);
        var callType = _callType.Value;

        // Start tracing span for this invocation
        using var activity = _activitySource.StartActivity(
            isFlow ? $"Flow: {className}.{methodName}" : $"Step: {className}.{methodName}",
            ActivityKind.Internal);
        
        activity?.SetTag("flow.id", _flowId);
        activity?.SetTag("flow.step", _currentStep);
        activity?.SetTag("flow.type", isFlow ? "flow" : "step");
        activity?.SetTag("flow.call_type", callType.ToString());
        activity?.SetTag("flow.is_await", isAwaitStep);

        if (isAwaitStep)
        {
            _logger.LogInformation("Detected [Await] step: {ClassName}.{MethodName}, callType={CallType}", 
                className, methodName, callType);
            activity?.AddEvent(new ActivityEvent($"Await step detected: {className}.{methodName}"));
        }

        if (isFlow)
        {
            _currentStep = 0;
            _logger.LogInformation("Starting flow: {ClassName}.{MethodName}", className, methodName);
            activity?.AddEvent(new ActivityEvent($"Flow started: {className}.{methodName}"));
        }

        Invocation? loggedInvocation = null;
        TimeSpan? remainingDelay = delay;

        if (callType == CallType.Resume)
        {
            loggedInvocation = await _executionLog!.GetLatestInvocationAsync(_flowId);
            if (loggedInvocation != null)
            {
                _currentStep = loggedInvocation.Step;
            }
        }
        else
        {
            loggedInvocation = await _executionLog!.GetInvocationAsync(_flowId, _currentStep);
        }

        // Check if step is already complete - replay it
        if (loggedInvocation != null)
        {
            if (loggedInvocation.ClassName != className || loggedInvocation.MethodName != methodName)
            {
                throw new InvalidOperationException("Incompatible change of flow structure");
            }

            if (loggedInvocation.Status == InvocationStatus.Complete)
            {
                _logger.LogInformation(
                    "Replaying completed step {Step}: {ClassName}.{MethodName} with args {Args} -> {Result}",
                    _currentStep, className, methodName, args, loggedInvocation.ReturnValue);
                _currentStep++;
                return loggedInvocation.ReturnValue;
            }
            else if (loggedInvocation.Status == InvocationStatus.WaitingForSignal && callType == CallType.Resume)
            {
                _logger.LogInformation("Resuming waiting step {Step}: {ClassName}.{MethodName}", 
                    _currentStep, className, methodName);

                var waitCondition = _waitConditions.GetOrAdd(_flowId, _ => new WaitCondition());
                await waitCondition.Semaphore.WaitAsync();
                waitCondition.Semaphore.Release();

                args = waitCondition.ResumeParameterValues ?? args;
            }
            else
            {
                // Retrying incomplete step
                _logger.LogInformation(
                    "Retrying incomplete step {Step} (attempt {Attempts}): {ClassName}.{MethodName} with args {Args}",
                    _currentStep, loggedInvocation.Attempts + 1, className, methodName, args);

                if (delay != null)
                {
                    var elapsed = DateTimeOffset.UtcNow - loggedInvocation.Timestamp;
                    remainingDelay = delay - elapsed;
                    if (remainingDelay < TimeSpan.Zero)
                    {
                        remainingDelay = TimeSpan.Zero;
                    }
                }
            }
        }

        // Log invocation start
        var status = (callType == CallType.Await || (isAwaitStep && callType != CallType.Resume))
            ? InvocationStatus.WaitingForSignal
            : InvocationStatus.Pending;
            
        await _executionLog!.LogInvocationStartAsync(
            _flowId,
            _currentStep,
            className,
            methodName,
            delay,
            status,
            (args as object[]) ?? args?.ToArray() ?? Array.Empty<object>());

        // Handle delayed execution
        if (delay != null && remainingDelay > TimeSpan.Zero)
        {
            _logger.LogInformation(
                "Delaying step {Step}: {ClassName}.{MethodName} for {Delay}",
                _currentStep, className, methodName, remainingDelay);

            activity?.AddEvent(new ActivityEvent($"Delaying for {remainingDelay}"));
            activity?.SetTag("flow.delay_seconds", remainingDelay.Value.TotalSeconds);

            await Task.Delay(remainingDelay.Value);
            
            activity?.AddEvent(new ActivityEvent("Delay completed"));
        }
        
        // Handle await steps (wait for external signal)
        if ((callType == CallType.Await || (isAwaitStep && callType != CallType.Resume)))
        {
            // Wait for external signal
            activity?.AddEvent(new ActivityEvent("Waiting for external signal (human-in-the-loop)"));
            var waitCondition = _waitConditions.GetOrAdd(_flowId, _ => new WaitCondition());
            
            _logger.LogInformation("Awaiting step {Step}: {ClassName}.{MethodName} - flow will pause here",
                _currentStep, className, methodName);

            // For Run mode with [Await] attribute, throw exception to stop flow execution
            if (callType == CallType.Run && isAwaitStep)
            {
                _logger.LogInformation("Flow {FlowId} paused at step {Step}, waiting for Resume()",
                    _flowId, _currentStep);
                activity?.AddEvent(new ActivityEvent("Flow paused - awaiting Resume()"));
                activity?.SetTag("flow.status", "paused");
                throw new FlowAwaitException($"Flow paused at {className}.{methodName}, waiting for external signal");
            }

            activity?.AddEvent(new ActivityEvent("Resuming from await"));
            await waitCondition.Semaphore.WaitAsync();
            args = waitCondition.ResumeParameterValues ?? args;
        }

        // Execute the actual method
        _logger.LogInformation("Executing step {Step}: {ClassName}.{MethodName} with args {Args}",
            _currentStep, className, methodName, args);

        var currentStepNumber = _currentStep;
        _currentStep++;

        object? result = null;
        try
        {
            result = targetMethod.Invoke(_target, args);

            // Handle async methods
            if (result is Task task)
            {
                await task;
                var resultProperty = task.GetType().GetProperty("Result");
                result = resultProperty?.GetValue(task);
            }
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }

        // Log completion
        loggedInvocation = await _executionLog.LogInvocationCompletionAsync(_flowId, currentStepNumber, result);

        _logger.LogInformation("Completed step {Step}: {ClassName}.{MethodName} -> {Result}",
            currentStepNumber, className, methodName, result);

        // Clean up wait conditions for completed flows
        if (loggedInvocation.IsFlow && loggedInvocation.Status == InvocationStatus.Complete)
        {
            _waitConditions.TryRemove(_flowId, out _);
        }

        return result;
    }

    public static void SignalResume(Guid flowId, object?[]? resumeArgs)
    {
        var waitCondition = _waitConditions.GetOrAdd(flowId, _ => new WaitCondition());
        waitCondition.ResumeParameterValues = resumeArgs;
        waitCondition.Semaphore.Release();
    }

    private static TimeSpan? GetDelay(StepAttribute? stepAttr)
    {
        if (stepAttr == null || stepAttr.Delay == long.MinValue)
        {
            return null;
        }

        return stepAttr.TimeUnit switch
        {
            TimeUnit.Seconds => TimeSpan.FromSeconds(stepAttr.Delay),
            TimeUnit.Minutes => TimeSpan.FromMinutes(stepAttr.Delay),
            TimeUnit.Hours => TimeSpan.FromHours(stepAttr.Delay),
            TimeUnit.Days => TimeSpan.FromDays(stepAttr.Delay),
            _ => null
        };
    }

    private class WaitCondition
    {
        public SemaphoreSlim Semaphore { get; } = new(0, 1);
        public object?[]? ResumeParameterValues { get; set; }
    }
}
