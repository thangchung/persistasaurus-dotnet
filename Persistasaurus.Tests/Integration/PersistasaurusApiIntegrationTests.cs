using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace Persistasaurus.Tests.Integration;

/// <summary>
/// Integration tests for the Persistasaurus API using .NET Aspire testing infrastructure.
/// Tests the API through HTTP endpoints using the AppHost orchestration.
/// </summary>
public class PersistasaurusApiIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private HttpClient? _httpClient;

    public async Task InitializeAsync()
    {
        // Create the AppHost using the testing builder
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Persistasaurus_AppHost>();

        // Build and start the application
        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        // Get the API resource and create an HTTP client for it
        _httpClient = _app.CreateHttpClient("api");
    }

    public async Task DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
        }
        
        _httpClient?.Dispose();
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        // Act
        var response = await _httpClient!.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<HealthCheckResponse>();
        Assert.NotNull(result);
        Assert.Equal("healthy", result.Status);
    }

    /// <summary>
    /// Test Scenario 1: HELLO WORLD FLOW EXAMPLE
    /// Tests basic flow execution with synchronous steps
    /// </summary>
    [Fact]
    public async Task HelloWorldFlow_CompletesSuccessfully()
    {
        // Act
        var response = await _httpClient!.PostAsync("/flows/hello-world", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<HelloWorldResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.FlowId);
        Assert.Contains("completed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test Scenario 2: USER SIGNUP FLOW WITH DELAYED EXECUTION
    /// Tests asynchronous flow execution with time-delayed steps (10 seconds)
    /// </summary>
    [Fact]
    public async Task SignupFlow_WithDelayedExecution_CompletesSuccessfully()
    {
        // Arrange
        var request = new SignupRequest("testuser", "test@example.com");

        // Act - Initiate signup
        var response = await _httpClient!.PostAsJsonAsync("/signups", request);

        // Assert - Signup initiated
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<SignupResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.FlowId);
        Assert.Equal("testuser", result.UserName);

        // Wait for delayed step to complete (10 seconds + buffer)
        await Task.Delay(TimeSpan.FromSeconds(12));

        // Verify signup status
        var statusResponse = await _httpClient!.GetAsync($"/signups/{result.FlowId}");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        
        var status = await statusResponse.Content.ReadFromJsonAsync<SignupStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal("testuser", status.UserName);
        Assert.Equal("test@example.com", status.Email);
    }

    /// <summary>
    /// Test Scenario 3: EMAIL CONFIRMATION (HUMAN IN THE LOOP)
    /// Tests flow resumption after external signal/human intervention
    /// </summary>
    [Fact]
    public async Task SignupFlow_WithEmailConfirmation_CompletesAfterResume()
    {
        // Arrange
        var request = new SignupRequest("confirmuser", "confirm@example.com");

        // Act - Step 1: Initiate signup
        var initiateResponse = await _httpClient!.PostAsJsonAsync("/signups", request);
        Assert.Equal(HttpStatusCode.Accepted, initiateResponse.StatusCode);
        
        var initiateResult = await initiateResponse.Content.ReadFromJsonAsync<SignupResponse>();
        Assert.NotNull(initiateResult);
        var flowId = initiateResult.FlowId;

        // Wait for delayed welcome email step (10 seconds + buffer)
        await Task.Delay(TimeSpan.FromSeconds(12));

        // Act - Step 2: Confirm email (human in the loop)
        var confirmation = new EmailConfirmation(DateTimeOffset.UtcNow);
        var confirmResponse = await _httpClient!.PostAsJsonAsync($"/signups/{flowId}/confirm", confirmation);

        // Assert - Email confirmation accepted
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        
        var confirmResult = await confirmResponse.Content.ReadFromJsonAsync<EmailConfirmationResponse>();
        Assert.NotNull(confirmResult);
        Assert.Equal(flowId, confirmResult.FlowId);
        Assert.Contains("confirmed", confirmResult.Message, StringComparison.OrdinalIgnoreCase);

        // Wait a bit for finalization step to complete
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Verify final signup status
        var statusResponse = await _httpClient!.GetAsync($"/signups/{flowId}");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        
        var status = await statusResponse.Content.ReadFromJsonAsync<SignupStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal("confirmuser", status.UserName);
        Assert.Equal("confirm@example.com", status.Email);
    }

    #region DTOs

    private record HealthCheckResponse(string Status);
    
    private record HelloWorldResponse(Guid FlowId, string Message);
    
    private record SignupRequest(string UserName, string Email);
    
    private record SignupResponse(Guid FlowId, string Message, string UserName);
    
    private record SignupStatusResponse(Guid FlowId, string UserName, string Email);
    
    private record EmailConfirmation(DateTimeOffset ConfirmedAt);
    
    private record EmailConfirmationResponse(Guid FlowId, string Message, DateTimeOffset ConfirmedAt);

    #endregion
}
