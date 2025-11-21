var builder = DistributedApplication.CreateBuilder(args);

// Add SQLite database
var sqlite = builder.AddSqlite("persistasaurus-db", "data", "localdb.db")
    .WithSqliteWeb();

// Add the Persistasaurus API
var api = builder.AddProject<Projects.Persistasaurus_Api>("api")
    .WithReference(sqlite)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithUrls(context =>
    {
        // Configure URL display for Scalar (OpenAPI)
        context.Urls.Add(new()
        {
            Url = "/scalar",
            DisplayText = "API Reference",
            Endpoint = context.GetEndpoint("https")
        });
    });

builder.Build().Run();
