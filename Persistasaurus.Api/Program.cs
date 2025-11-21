using System.Diagnostics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Persistasaurus.Features.Flows;
using Scalar.AspNetCore;
using PersistasaurusEngine = Persistasaurus.Core.Persistasaurus;

// Create ActivitySource for custom tracing
var activitySource = new ActivitySource("Persistasaurus.Api", "1.0.0");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddOpenApi();

// Add SQLite connection from Aspire
builder.AddSqliteConnection("persistasaurus-db");

// Configure OpenTelemetry with tracing, metrics, and logging
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Persistasaurus.Api"))
    .WithTracing(tracing => tracing
        .AddSource("Persistasaurus.Api")
        .AddSource("Persistasaurus.FlowInterceptor")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("Persistasaurus.Api")
        .AddMeter("Persistasaurus.FlowInterceptor")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

// Add OpenTelemetry logging (structured logs)
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("Persistasaurus API")
            .WithTheme(ScalarTheme.Purple)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("HealthCheck")
    ;


// Storage for active signups (in a real app, this would be a database)
var activeSignups = new Dictionary<Guid, SignupData>();

// ===== HELLO WORLD FLOW EXAMPLE =====
app.MapPost("/flows/hello-world", () =>
{
    using var activity = activitySource.StartActivity("HelloWorldFlow", ActivityKind.Server);

    var flowId = Guid.NewGuid();
    activity?.SetTag("flow.id", flowId);
    activity?.SetTag("flow.type", "hello_world");
    activity?.AddEvent(new ActivityEvent("Starting Hello World flow"));

    var flow = PersistasaurusEngine.GetFlow<IHelloWorldFlow>(flowId);

    // Run synchronously
    flow.Run(f => f.SayHello());

    activity?.AddEvent(new ActivityEvent("Hello World flow completed"));
    activity?.SetTag("flow.status", "completed");

    return Results.Ok(new { flowId, message = "Hello World flow completed!" });
})
.WithName("HelloWorldFlow")
;

// ===== USER SIGNUP FLOW WITH DELAYED EXECUTION =====
app.MapPost("/signups", async (SignupRequest request) =>
{
    using var activity = activitySource.StartActivity("SignupFlow", ActivityKind.Server);

    var flowId = Guid.NewGuid();
    activity?.SetTag("flow.id", flowId);
    activity?.SetTag("flow.type", "signup_with_delay");
    activity?.SetTag("user.name", request.UserName);
    activity?.SetTag("user.email", request.Email);
    activity?.AddEvent(new ActivityEvent($"Initiating signup for {request.UserName}"));

    // Store signup data
    activeSignups[flowId] = new SignupData(request.UserName, request.Email);

    var flow = PersistasaurusEngine.GetFlow<ISignupFlow>(flowId);

    // Execute each step through the proxy - they will be logged and can be replayed
    _ = Task.Run(async () =>
    {
        try
        {
            var userId = flow.Execute(f => f.CreateUserRecord(request.UserName, request.Email));
            activeSignups[flowId] = activeSignups[flowId] with { UserId = userId };

            flow.Run(f => f.SendWelcomeEmail(userId, request.Email));

            // This step has [Await] - it will pause execution here
            flow.Run(f => f.ConfirmEmailAddress(default));

            // This will only execute after Resume() is called
            flow.Run(f => f.FinalizeSignup(userId));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Flow {flowId} error: {ex.Message}");
        }
    });

    return Results.Accepted($"/signups/{flowId}", new
    {
        flowId,
        message = "Signup initiated. Email will be sent after delay.",
        userName = request.UserName
    });
})
.WithName("InitiateSignup")
;

// ===== EMAIL CONFIRMATION (HUMAN IN THE LOOP) =====
app.MapPost("/signups/{flowId:guid}/confirm", async (Guid flowId) =>
{
    var confirmAt = DateTimeOffset.UtcNow;

    using var activity = activitySource.StartActivity("ConfirmEmail", ActivityKind.Server);
    activity?.SetTag("flow.id", flowId);
    activity?.SetTag("flow.type", "email_confirmation");
    activity?.SetTag("confirmation.time", confirmAt);
    activity?.AddEvent(new ActivityEvent($"Resuming flow {flowId} with email confirmation"));

    if (!activeSignups.ContainsKey(flowId))
    {
        activity?.SetTag("flow.status", "not_found");
        return Results.NotFound(new { error = "Signup not found" });
    }

    var flow = PersistasaurusEngine.GetFlow<ISignupFlow>(flowId);

    activity?.AddEvent(new ActivityEvent("Signaling resume to paused flow"));

    // Resume the waiting flow with confirmation data
    await Task.Run(() =>
    {
        flow.SignalResume(confirmAt);
        flow.Resume(f => f.ConfirmEmailAddress(confirmAt));
    });

    activity?.AddEvent(new ActivityEvent("Flow resumed successfully"));
    activity?.SetTag("flow.status", "resumed");

    return Results.Ok(new
    {
        flowId,
        message = "Email confirmed! Signup will be finalized.",
        confirmedAt = confirmAt
    });
})
.WithName("ConfirmEmail")
;

// ===== GET SIGNUP STATUS =====
app.MapGet("/signups/{flowId:guid}", (Guid flowId) =>
{
    if (!activeSignups.TryGetValue(flowId, out var data))
    {
        return Results.NotFound(new { error = "Signup not found" });
    }

    return Results.Ok(new { flowId, userName = data.UserName, email = data.Email });
})
.WithName("GetSignupStatus")
;

app.Run();

// ===== FLOW INTERFACES =====
public interface IHelloWorldFlow
{
    [Flow]
    void SayHello();

    [Step]
    int Say(string name, int count);
}

public interface ISignupFlow
{
    [Flow]
    void SignUp(string userName, string email);

    [Step]
    long CreateUserRecord(string userName, string email);

    [Step(Delay = 10, TimeUnit = TimeUnit.Seconds)] // 10 second delay for demo
    void SendWelcomeEmail(long userId, string email);

    [Step]
    [Await] // Wait for external confirmation signal (human in the loop)
    void ConfirmEmailAddress(DateTimeOffset confirmedAt);

    [Step]
    void FinalizeSignup(long userId);
}

// ===== FLOW IMPLEMENTATIONS =====
public class HelloWorldFlow : IHelloWorldFlow
{
    public void SayHello()
    {
        int sum = 0;
        for (int i = 0; i < 5; i++)
        {
            sum += Say("World", i);
        }
        Console.WriteLine($"Sum: {sum}");
    }

    public int Say(string name, int count)
    {
        Console.WriteLine($"Hello, {name} ({count})");
        return count;
    }
}

public class SignupFlow : ISignupFlow
{
    // The actual flow logic is in the step methods, called through the proxy
    // This main flow method just orchestrates by calling each step through the flow parameter
    public void SignUp(string userName, string email)
    {
        // This method won't actually execute - it's just a marker
        // The real execution happens through the FlowInterceptor
        throw new NotImplementedException("SignUp should not be called directly - use through FlowInstance");
    }

    public long CreateUserRecord(string userName, string email)
    {
        var userId = Random.Shared.NextInt64(1000, 9999);
        Console.WriteLine($"Created user record: {userId} for {userName} ({email})");
        return userId;
    }

    public void SendWelcomeEmail(long userId, string email)
    {
        Console.WriteLine($"Sending welcome email to {email} for user {userId}");
        // Simulate email sending
        Console.WriteLine($"✉️ Email sent! Please confirm your email address.");
    }

    public void ConfirmEmailAddress(DateTimeOffset confirmedAt)
    {
        Console.WriteLine($"Email confirmed at {confirmedAt}");
    }

    public void FinalizeSignup(long userId)
    {
        Console.WriteLine($"✅ Signup finalized for user {userId}");
    }
}

// ===== DTOs =====
public record SignupRequest(string UserName, string Email);
public record SignupData(string UserName, string Email, long? UserId = null);
