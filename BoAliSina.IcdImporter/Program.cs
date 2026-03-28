using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using BoAliSina.IcdImporter.Repositories;
using BoAliSina.IcdImporter.Services;

var services = new ServiceCollection();

// Configuration (Use environment variables or appsettings.json in production)
// Configuration: Update These for your local Neo4j instance
const string neo4jUri = "neo4j://127.0.0.1:7687"; 
const string neo4jUser = "neo4j";
const string neo4jPass = "BoAliSina2026"; 
const string rootIcdUri = "https://id.who.int/icd/release/11/2024-01/mms"; // Valid 2024-01 release root
const string clientId = "463e892c-5c6e-479d-93bf-e7f0ad95b951_53e535aa-9234-4264-9398-46e8ef507af0";
const string clientSecret = "ammUHSP6fyOTh8QGCAPeSBbqrNVyiaZbUND1Fmc1lpc=";

services.AddHttpClient<IIcdApiClient, IcdApiClient>(client => new IcdApiClient(client, clientId, clientSecret));
services.AddSingleton<IIcdGraphRepository>(new Neo4jIcdRepository(neo4jUri, neo4jUser, neo4jPass));
services.AddScoped<IcdImportService>();

var serviceProvider = services.BuildServiceProvider();

Console.WriteLine("Starting ICD to Neo4j Import...");

try
{
    var repository = serviceProvider.GetRequiredService<IIcdGraphRepository>();
    await repository.CreateConstraintsAsync();

    var importer = serviceProvider.GetRequiredService<IcdImportService>();
    
    // In a real scenario, you'd get a token here
    // var apiClient = (IcdApiClient)serviceProvider.GetRequiredService<IIcdApiClient>();
    // apiClient.SetToken("YOUR_ACCESS_TOKEN");

    await importer.ImportConceptHierarchyAsync(rootIcdUri);
    
    Console.WriteLine("Import completed successfully.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error during import: {ex}");
}
finally
{
    if (serviceProvider is IAsyncDisposable disposable)
        await disposable.DisposeAsync();
}
