# Persistasaurus.NET 🦖

A minimal durable execution library for .NET 10, inspired by [Persistasaurus](https://github.com/gunnarmorling/persistasaurus) for Java. Persistasaurus stores method invocations in a SQLite database, enabling flows to resume from the last successful step after failures or restarts.

> https://www.morling.dev/blog/building-durable-execution-engine-with-sqlite/

## Features

✅ **Durable Execution**: Persists flow execution state in SQLite  
✅ **Step Replay**: Automatically replays completed steps without re-execution  
✅ **Delayed Execution**: Schedule steps to run after a specified delay  
✅ **Human-in-the-Loop**: Support for external signals to resume flows  
✅ **Async/Await Native**: Built on .NET's Task-based async model  
✅ **Interface-based**: Uses DispatchProxy for transparent interception  
✅ **No External Dependencies**: Only standard .NET packages (EF Core, SQLite)  
✅ **.NET Aspire Integration**: Cloud-native orchestration with AppHost  
✅ **Scalar OpenAPI**: Interactive API documentation  
✅ **Comprehensive Testing**: Unit and integration tests with Aspire.Hosting.Testing

## Architecture

Persistasaurus.NET follows a **vertical slice architecture**:

```
Persistasaurus.NET/
├── Persistasaurus/              # Core library
│   ├── Features/
│   │   ├── Flows/              # Flow & Step attributes, FlowInstance
│   │   ├── Execution/          # ExecutionLog, Invocation, InvocationStatus
│   │   ├── Interception/       # FlowInterceptor using DispatchProxy
│   │   └── Recovery/           # Incomplete flow recovery
│   ├── Core/                   # Main Persistasaurus class
│   └── Data/                   # EF Core DbContext & entities
├── Persistasaurus.Api/          # Minimal API demo with Scalar
├── Persistasaurus.AppHost/      # .NET Aspire orchestration
└── Persistasaurus.Tests/
    ├── Unit/                   # Unit tests
    └── Integration/            # Aspire-based integration tests
```

## Quick Start

### 1. Define Your Flow Interface

```csharp
using Persistasaurus.Features.Flows;

public interface IHelloWorldFlow
{
    [Flow]
    void SayHello();
    
    [Step]
    int Say(string name, int count);
}
```

### 2. Implement the Flow

```csharp
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
```

### 3. Execute the Flow

```csharp
using Persistasaurus.Core;

var flowId = Guid.NewGuid();
var flow = Persistasaurus.GetFlow<IHelloWorldFlow>(flowId);

// Run synchronously
flow.Run(f => f.SayHello());

// Or run asynchronously
await flow.RunAsync(f => f.SayHello());
```

## Delayed Execution

Add delays to steps using the `Step` attribute:

```csharp
public interface ISignupFlow
{
    [Flow]
    void SignUp(string userName, string email);
    
    [Step]
    long CreateUserRecord(string userName, string email);
    
    [Step(Delay = 3, TimeUnit = TimeUnit.Days)]
    void SendWelcomeEmail(long userId, string email);
}
```

## Human-in-the-Loop

For flows that require external input:

```csharp
public interface IApprovalFlow
{
    [Flow]
    void ProcessRequest();
    
    [Step]
    void SubmitRequest();
    
    [Step]
    void AwaitApproval(bool approved);  // Waits for external signal
    
    [Step]
    void FinalizeRequest();
}
```

**Resume the flow:**

```csharp
var flow = Persistasaurus.GetFlow<IApprovalFlow>(flowId);

// Signal with approval data
flow.SignalResume(true);

// Resume execution
flow.Resume(f => f.AwaitApproval(true));
```

## Running with .NET Aspire

The recommended way to run Persistasaurus.NET is using the **AppHost**:

```bash
cd Persistasaurus.AppHost
dotnet run
```

This launches:
- **Persistasaurus API** at the configured HTTPS endpoint
- **Aspire Dashboard** at `https://localhost:15xxx` (URL shown in console)
- **Scalar API Docs** at `https://localhost:7xxx/scalar` (interactive OpenAPI UI)

The AppHost uses **file-based program style** (`apphost.cs` instead of `Program.cs`) and provides:
- Service discovery and orchestration
- Telemetry and observability
- Health checks and monitoring

### Alternative: Run API Directly

```bash
cd Persistasaurus.Api
dotnet run
```

Then navigate to `/scalar` for API documentation.

## API Endpoints

The `Persistasaurus.Api` project includes three complete flow examples:

**1. Hello World Flow**
- `POST /flows/hello-world` - Simple synchronous flow demo

**2. User Signup Flow (with Delayed Execution)**
- `POST /signups` - Initiate signup with 10-second delayed email
- `GET /signups/{id}` - Get signup status

**3. Email Confirmation (Human-in-the-Loop)**
- `POST /signups/{id}/confirm` - Confirm email and resume flow

### Example Usage

```bash
# 1. Start signup flow
curl -X POST https://localhost:7xxx/signups \
  -H "Content-Type: application/json" \
  -d '{"userName":"alice","email":"alice@example.com"}'
# Returns: { "flowId": "guid", "status": "User signed up", "userId": 123 }

# 2. Wait for email (10 seconds) or confirm manually
curl -X POST https://localhost:7xxx/signups/{flowId}/confirm \
  -H "Content-Type: application/json" \
  -d '{"confirmedAt":"2025-11-21T10:00:00Z"}'
# Returns: { "status": "Email confirmed", "userId": 123 }

# 3. Check status
curl https://localhost:7xxx/signups/{flowId}
```

### Scalar OpenAPI Documentation

Navigate to `/scalar` in your browser for:
- Interactive API testing
- Request/response schemas
- Authentication examples
- Live API exploration

Features:
- Delayed email sending (10-second step delay)
- Email confirmation (human-in-the-loop)
- Automatic recovery of incomplete flows on restart

## How It Works

### 1. **Proxy Interception**

Uses .NET's `DispatchProxy` to intercept all method calls on flow interfaces.

### 2. **Execution Log**

Each step invocation is logged to SQLite with:
- Flow ID & Step number
- Method name & parameters
- Execution status (Pending, Complete, WaitingForSignal)
- Return value (JSON serialized)
- Attempt count

### 3. **Replay Logic**

When a flow restarts:
1. Check execution log for completed steps
2. Replay results from log (skip re-execution)
3. Resume from first incomplete step
4. Continue execution

### 4. **Delayed Execution**

For delayed steps:
1. Log step with delay duration
2. Use `Task.Delay()` to wait
3. Execute step after delay completes

### 5. **Recovery**

On startup, Persistasaurus automatically:
1. Queries for incomplete flows (step 0, status != Complete)
2. Schedules them for async execution
3. Resumes from last known state

## Database Schema

SQLite table structure:

```sql
CREATE TABLE execution_log (
    FlowId TEXT NOT NULL,
    Step INTEGER NOT NULL,
    Timestamp INTEGER NOT NULL,
    ClassName TEXT NOT NULL,
    MethodName TEXT NOT NULL,
    Delay INTEGER,
    Status TEXT CHECK(Status IN ('Pending','WaitingForSignal','Complete')),
    Attempts INTEGER DEFAULT 1,
    Parameters TEXT,  -- JSON
    ReturnValue TEXT, -- JSON
    PRIMARY KEY (FlowId, Step)
);
```

## Design Decisions

### Why Interface-Based?

.NET's `DispatchProxy` requires interfaces. Unlike Java's ByteBuddy which can proxy classes, DispatchProxy is the zero-dependency option for method interception in .NET.

### Why JSON Serialization?

JSON is human-readable and makes debugging easier. You can query the SQLite database and understand the data without deserialization.

### Why AsyncLocal vs ScopedValue?

.NET's `AsyncLocal<T>` is equivalent to Java 25's ScopedValue - it provides context that flows through async call chains.

### Why Task.Delay vs Virtual Threads?

.NET doesn't have virtual threads (yet), but `Task` and `async/await` provide similar benefits - lightweight, non-blocking concurrency.

## Comparison to Java Version

| Feature | Java (Persistasaurus) | .NET (Persistasaurus.NET) |
|---------|----------------------|---------------------------|
| **Proxy** | ByteBuddy (class proxy) | DispatchProxy (interface proxy) |
| **Async** | Virtual Threads (Java 21) | Task/async-await |
| **Context** | ScopedValue (Java 25) | AsyncLocal<T> |
| **Serialization** | Java Serialization | System.Text.Json |
| **Persistence** | JDBC + SQLite | EF Core + SQLite |
| **Attributes** | @Flow / @Step | [Flow] / [Step] |

## Testing

Persistasaurus.NET includes **unit tests** and **integration tests** organized in separate folders:

```
Persistasaurus.Tests/
├── Unit/                # Fast, isolated unit tests
│   └── BasicFlowTests.cs
└── Integration/         # Full API scenarios with Aspire
    └── PersistasaurusApiIntegrationTests.cs
```

### Run All Tests

```bash
dotnet test
```

### Run Unit Tests Only

```bash
dotnet test --filter "FullyQualifiedName~Unit"
```

### Run Integration Tests Only

```bash
dotnet test --filter "FullyQualifiedName~Integration"
```

### Integration Tests

The integration tests use **Aspire.Hosting.Testing** to:
1. Start the AppHost programmatically
2. Create HTTP clients for services
3. Test end-to-end scenarios
4. Cleanup resources automatically

**Test Scenarios:**

1. **Health Check** - Verifies API is healthy
2. **Hello World Flow** - Basic synchronous flow execution
3. **Signup with Delayed Execution** - Tests 10-second delayed email step
4. **Email Confirmation (Human-in-the-Loop)** - Tests await/resume pattern

Example integration test pattern:

```csharp
public class PersistasaurusApiIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Persistasaurus_AppHost>();
        _app = await appHost.BuildAsync();
        await _app.StartAsync();
        _client = _app.CreateHttpClient("api");
    }

    [Fact]
    public async Task SignupFlow_WithDelayedExecution_CompletesSuccessfully()
    {
        // Test delayed execution logic
    }
}
```

Tests cover:
- Basic flow execution
- Step replay after failure
- Delayed execution (10 second delay)
- Human-in-the-loop (await/resume)
- Incomplete flow recovery
- API endpoint integration

## Limitations

1. **Interface-Required**: All flows must be defined as interfaces
2. **No Overloads**: Flow methods cannot be overloaded (method name must be unique)
3. **Serializable Parameters**: All parameters/return values must be JSON serializable
4. **Single-Writer**: SQLite limits concurrent writes (fine for single-instance apps)

## .NET Aspire Features

### File-Based Program Style

The AppHost uses **file-based program** pattern (inspired by [aspire-13-samples](https://github.com/davidfowl/aspire-13-samples)):

```csharp
// apphost.cs (not Program.cs)
var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Persistasaurus_Api>("api")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithUrls(context =>
    {
        context.Urls.Add(new()
        {
            Url = "/scalar",
            DisplayText = "API Reference",
            Endpoint = context.GetEndpoint("https")
        });
    });

builder.Build().Run();
```

### Aspire Dashboard

The dashboard provides:
- **Live Telemetry**: Traces, metrics, and logs from all services
- **Resource Management**: View running services, containers, and projects
- **Health Monitoring**: Real-time health check status
- **Environment Variables**: Configuration inspection

Access at `https://localhost:15xxx` (URL shown when starting AppHost).

## Future Enhancements

- [ ] Support for Saga patterns with compensation
- [ ] Retry policies with exponential backoff
- [ ] Parallel step execution
- [ ] Flow versioning and migration
- [ ] Admin UI for flow management
- [ ] Azure deployment templates (Container Apps, Functions)
- [ ] PostgreSQL support for multi-instance scenarios

## Resources

- [Original Java Persistasaurus](https://github.com/gunnarmorling/persistasaurus) by Gunnar Morling
- [Blog: Building a Durable Execution Engine With SQLite](https://www.morling.dev/blog/building-durable-execution-engine-with-sqlite/)
- [.NET Aspire Documentation](https://aspire.dev/)
- [DispatchProxy Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.dispatchproxy)
- [Scalar OpenAPI Specification](https://github.com/scalar/scalar)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)

## License

MIT License - see [Gunnar Morling's original project](https://github.com/gunnarmorling/persistasaurus)

## Credits

**Inspired by:** Gunnar Morling's excellent blog post and Java implementation  
**Built with:** .NET 10, EF Core, .NET Aspire, Scalar  
**Architecture:** Vertical Slice Architecture with minimal dependencies
