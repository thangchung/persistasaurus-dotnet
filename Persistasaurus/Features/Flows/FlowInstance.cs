using Persistasaurus.Features.Interception;

namespace Persistasaurus.Features.Flows;

/// <summary>
/// Represents one execution of a flow with a specific ID.
/// </summary>
/// <typeparam name="T">The flow interface type.</typeparam>
public class FlowInstance<T> where T : class
{
    private readonly Guid _id;
    private readonly T _flow;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowInstance{T}"/> class.
    /// </summary>
    /// <param name="id">The unique identifier for this flow execution.</param>
    /// <param name="flow">The flow proxy instance.</param>
    internal FlowInstance(Guid id, T flow)
    {
        _id = id;
        _flow = flow;
    }

    /// <summary>
    /// Gets the unique identifier for this flow execution.
    /// </summary>
    public Guid Id => _id;

    /// <summary>
    /// Runs the flow synchronously.
    /// </summary>
    /// <param name="flowAction">The action to execute on the flow.</param>
    public void Run(Action<T> flowAction)
    {
        try
        {
            FlowInterceptor<T>.SetCallType(CallType.Run);
            flowAction(_flow);
        }
        catch (Interception.FlowAwaitException)
        {
            // Expected: flow paused at an [Await] step, waiting for external signal
            // This is not an error - the flow will resume when Resume() is called
        }
    }

    /// <summary>
    /// Executes the flow synchronously and returns a result.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="flowFunction">The function to execute on the flow.</param>
    /// <returns>The result of the flow execution.</returns>
    public TResult Execute<TResult>(Func<T, TResult> flowFunction)
    {
        FlowInterceptor<T>.SetCallType(CallType.Run);
        return flowFunction(_flow);
    }

    /// <summary>
    /// Runs the flow asynchronously.
    /// </summary>
    /// <param name="flowAction">The action to execute on the flow.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RunAsync(Action<T> flowAction)
    {
        return Task.Run(() =>
        {
            try
            {
                FlowInterceptor<T>.SetCallType(CallType.Run);
                flowAction(_flow);
            }
            catch (Interception.FlowAwaitException)
            {
                // Expected: flow paused at an [Await] step, waiting for external signal
                // This is not an error - the flow will resume when Resume() is called
            }
        });
    }

    /// <summary>
    /// Executes the flow asynchronously and returns a result.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="flowFunction">The function to execute on the flow.</param>
    /// <returns>A task representing the asynchronous operation with a result.</returns>
    public Task<TResult> ExecuteAsync<TResult>(Func<T, TResult> flowFunction)
    {
        return Task.Run(() =>
        {
            FlowInterceptor<T>.SetCallType(CallType.Run);
            return flowFunction(_flow);
        });
    }

    /// <summary>
    /// Resumes a flow that is waiting for an external signal.
    /// </summary>
    /// <param name="flowAction">The action to execute on the flow.</param>
    public void Resume(Action<T> flowAction)
    {
        FlowInterceptor<T>.SetCallType(CallType.Resume);
        flowAction(_flow);
    }

    /// <summary>
    /// Signals that a waiting step should resume with the provided arguments.
    /// </summary>
    /// <param name="resumeArgs">The arguments to pass to the waiting step.</param>
    public void SignalResume(params object?[]? resumeArgs)
    {
        FlowInterceptor<T>.SignalResume(_id, resumeArgs);
    }
}
