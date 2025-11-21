using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistasaurus.Data;

namespace Persistasaurus.Features.Execution;

/// <summary>
/// Manages the persistent execution log using SQLite and Entity Framework Core.
/// </summary>
public class ExecutionLog
{
    private static readonly Lazy<ExecutionLog> _instance = new(() => new ExecutionLog());
    private readonly ILogger<ExecutionLog> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Gets the singleton instance of the ExecutionLog.
    /// </summary>
    public static ExecutionLog Instance => _instance.Value;

    private ExecutionLog()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole())
            .CreateLogger<ExecutionLog>();

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Ensure database is created
        using var context = new PersistasaurusDbContext();
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Logs the start of a flow or step invocation.
    /// </summary>
    public async Task LogInvocationStartAsync(
        Guid flowId,
        int step,
        string className,
        string methodName,
        TimeSpan? delay,
        InvocationStatus status,
        object[]? parameters)
    {
        await using var context = new PersistasaurusDbContext();

        var existing = await context.ExecutionLog
            .FindAsync(flowId.ToString(), step);

        if (existing != null)
        {
            // Increment attempts on retry
            existing.Attempts++;
            _logger.LogInformation(
                "Retrying flow {FlowId} step {Step}: {ClassName}.{MethodName} (attempt {Attempts})",
                flowId, step, className, methodName, existing.Attempts);
        }
        else
        {
            // Create new entry
            var entry = new ExecutionLogEntry
            {
                FlowId = flowId.ToString(),
                Step = step,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ClassName = className,
                MethodName = methodName,
                Delay = delay?.TotalMilliseconds is double ms ? (long)ms : null,
                Status = status,
                Attempts = 1,
                Parameters = parameters != null ? JsonSerializer.Serialize(parameters, _jsonOptions) : null
            };

            context.ExecutionLog.Add(entry);
            _logger.LogInformation(
                "Logging invocation start: flow {FlowId} step {Step}: {ClassName}.{MethodName}",
                flowId, step, className, methodName);
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Logs the completion of a flow or step invocation.
    /// </summary>
    public async Task<Invocation> LogInvocationCompletionAsync(
        Guid flowId,
        int step,
        object? returnValue)
    {
        await using var context = new PersistasaurusDbContext();

        var entry = await context.ExecutionLog
            .FindAsync(flowId.ToString(), step);

        if (entry == null)
        {
            throw new InvalidOperationException(
                $"No invocation found with flowId={flowId} and step={step}");
        }

        entry.Status = InvocationStatus.Complete;
        entry.ReturnValue = returnValue != null ? JsonSerializer.Serialize(returnValue, _jsonOptions) : null;

        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Completed flow {FlowId} step {Step}: {ClassName}.{MethodName} -> {ReturnValue}",
            flowId, step, entry.ClassName, entry.MethodName, returnValue);

        return MapToInvocation(entry);
    }

    /// <summary>
    /// Gets a specific invocation by flow ID and step.
    /// </summary>
    public async Task<Invocation?> GetInvocationAsync(Guid flowId, int step)
    {
        await using var context = new PersistasaurusDbContext();

        var entry = await context.ExecutionLog
            .FindAsync(flowId.ToString(), step);

        return entry != null ? MapToInvocation(entry) : null;
    }

    /// <summary>
    /// Gets the latest invocation for a flow.
    /// </summary>
    public async Task<Invocation?> GetLatestInvocationAsync(Guid flowId)
    {
        await using var context = new PersistasaurusDbContext();

        var entry = await context.ExecutionLog
            .Where(e => e.FlowId == flowId.ToString())
            .OrderByDescending(e => e.Step)
            .FirstOrDefaultAsync();

        return entry != null ? MapToInvocation(entry) : null;
    }

    /// <summary>
    /// Gets all incomplete flows (status != Complete, step = 0).
    /// </summary>
    public async Task<List<Invocation>> GetIncompleteFlowsAsync()
    {
        await using var context = new PersistasaurusDbContext();

        var entries = await context.ExecutionLog
            .Where(e => e.Step == 0 && e.Status != InvocationStatus.Complete)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        return entries.Select(MapToInvocation).ToList();
    }

    /// <summary>
    /// Resets the execution log (for testing purposes).
    /// </summary>
    public async Task ResetAsync()
    {
        await using var context = new PersistasaurusDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
        _logger.LogInformation("Execution log reset");
    }

    private Invocation MapToInvocation(ExecutionLogEntry entry)
    {
        object[]? parameters = null;
        if (!string.IsNullOrEmpty(entry.Parameters))
        {
            parameters = JsonSerializer.Deserialize<object[]>(entry.Parameters, _jsonOptions);
        }

        object? returnValue = null;
        if (!string.IsNullOrEmpty(entry.ReturnValue))
        {
            returnValue = JsonSerializer.Deserialize<object>(entry.ReturnValue, _jsonOptions);
        }

        return new Invocation(
            Guid.Parse(entry.FlowId),
            entry.Step,
            DateTimeOffset.FromUnixTimeMilliseconds(entry.Timestamp),
            entry.ClassName,
            entry.MethodName,
            entry.Status,
            entry.Attempts,
            parameters,
            returnValue);
    }
}
