using Persistasaurus.Core;
using Persistasaurus.Features.Execution;
using Persistasaurus.Features.Flows;

namespace Persistasaurus.Tests;

public class PersistasaurusTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Reset database before each test
        await ExecutionLog.Instance.ResetAsync();
        
        // Reset static state
        ReliableFlow.ShouldFail = false;
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ShouldRunFlowSuccessfully()
    {
        // Arrange
        var flowId = Guid.NewGuid();
        var flow = Persistasaurus.Core.Persistasaurus.GetFlow<ISimpleFlow>(flowId);

        // Act - Call steps externally (workaround for DispatchProxy limitation)
        flow.Run(f => f.ExecuteWorkflow());

        // Assert - Flow method should be logged
        var flowInvocation = await ExecutionLog.Instance.GetInvocationAsync(flowId, 0);
        Assert.NotNull(flowInvocation);
        Assert.Equal(nameof(ISimpleFlow.ExecuteWorkflow), flowInvocation.MethodName);
        Assert.Equal(InvocationStatus.Complete, flowInvocation.Status);
        Assert.Equal(1, flowInvocation.Attempts);
    }

    [Fact]
    public async Task ShouldExecuteFlowWithReturnValue()
    {
        // Arrange
        var flowId = Guid.NewGuid();
        var flow = Persistasaurus.Core.Persistasaurus.GetFlow<ICalculatorFlow>(flowId);

        // Act
        var result = flow.Execute(f => f.Calculate());

        // Assert - Should return 10
        Assert.Equal(10, result);
        
        var flowInvocation = await ExecutionLog.Instance.GetInvocationAsync(flowId, 0);
        Assert.NotNull(flowInvocation);
        Assert.Equal(InvocationStatus.Complete, flowInvocation.Status);
    }

    [Fact]
    public async Task ShouldRunFlowAsynchronously()
    {
        // Arrange
        var flowId = Guid.NewGuid();
        var flow = Persistasaurus.Core.Persistasaurus.GetFlow<ISimpleFlow>(flowId);

        // Act
        await flow.RunAsync(f => f.ExecuteWorkflow());

        // Assert - Give async execution time to complete
        await Task.Delay(100);
        
        var flowInvocation = await ExecutionLog.Instance.GetInvocationAsync(flowId, 0);
        Assert.NotNull(flowInvocation);
        Assert.Equal(InvocationStatus.Complete, flowInvocation.Status);
    }

    [Fact]
    public async Task ShouldReplayCompletedStepsOnRetry()
    {
        // Arrange - Run flow with failure
        ReliableFlow.ShouldFail = true;
        var flowId = Guid.NewGuid();
        var flow = Persistasaurus.Core.Persistasaurus.GetFlow<IReliableFlow>(flowId);

        // Act - First execution should fail
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            flow.Run(f => f.ProcessData());
        });
        Assert.Contains("Simulated failure", exception.Message);

        // Verify flow started but not completed
        var failedInvocation = await ExecutionLog.Instance.GetInvocationAsync(flowId, 0);
        Assert.NotNull(failedInvocation);
        Assert.Equal(InvocationStatus.Pending, failedInvocation.Status);
        Assert.Equal(1, failedInvocation.Attempts);

        // Act - Retry with fix
        ReliableFlow.ShouldFail = false;
        flow.Run(f => f.ProcessData());

        // Assert - Should complete on retry
        var retriedInvocation = await ExecutionLog.Instance.GetInvocationAsync(flowId, 0);
        Assert.NotNull(retriedInvocation);
        Assert.Equal(InvocationStatus.Complete, retriedInvocation.Status);
        Assert.Equal(2, retriedInvocation.Attempts);
    }
}

// ===== TEST FLOW INTERFACES =====
public interface ISimpleFlow
{
    [Flow]
    void ExecuteWorkflow();
}

public interface ICalculatorFlow
{
    [Flow]
    int Calculate();
}

public interface IReliableFlow
{
    [Flow]
    void ProcessData();
}

// ===== TEST FLOW IMPLEMENTATIONS =====
public class SimpleFlow : ISimpleFlow
{
    public void ExecuteWorkflow()
    {
        Console.WriteLine("Executing simple workflow");
        Console.WriteLine("Step 1: Initialize");
        Console.WriteLine("Step 2: Process");
        Console.WriteLine("Step 3: Complete");
    }
}

public class CalculatorFlow : ICalculatorFlow
{
    public int Calculate()
    {
        Console.WriteLine("Calculating: 5 + 5");
        return 10;
    }
}

public class ReliableFlow : IReliableFlow
{
    public static bool ShouldFail = false;

    public void ProcessData()
    {
        Console.WriteLine("Processing data...");
        
        if (ShouldFail)
        {
            throw new InvalidOperationException("Simulated failure");
        }
        
        Console.WriteLine("Data processed successfully");
    }
}
