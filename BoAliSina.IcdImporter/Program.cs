using Microsoft.Extensions.DependencyInjection;
using BoAliSina.IcdImporter.Repositories;
using BoAliSina.IcdImporter.Models.Graph;
using BoAliSina.IcdImporter.Services;
using Spectre.Console;

// Initialize Services
var services = new ServiceCollection();

const string neo4jUri = "neo4j://127.0.0.1:7687"; 
const string neo4jUser = "neo4j";
const string neo4jPass = "BoAliSina2026"; 
const string rootIcdUri = "https://id.who.int/icd/release/11/2024-01/mms";
const string clientId = "463e892c-5c6e-479d-93bf-e7f0ad95b951_53e535aa-9234-4264-9398-46e8ef507af0";
const string clientSecret = "ammUHSP6fyOTh8QGCAPeSBbqrNVyiaZbUND1Fmc1lpc=";

services.AddHttpClient<IIcdApiClient, IcdApiClient>(client => new IcdApiClient(client, clientId, clientSecret));
services.AddSingleton<INeo4jRepository>(new Neo4jRepository(neo4jUri, neo4jUser, neo4jPass));
services.AddSingleton<ISymptomNormalizationService, SymptomNormalizationService>();
services.AddScoped<IcdImportService>();

var serviceProvider = services.BuildServiceProvider();

// --- UI Logic ---
AnsiConsole.Clear();
AnsiConsole.Write(
    new FigletText("BoAliSina")
        .Centered()
        .Color(Color.Purple));

AnsiConsole.Write(
    new Panel(Align.Center(new Markup("[bold white]Medical Knowledge Graph Importer & Search[/]\n[grey]Graph-Based System (ICD-11 + Neo4j)[/]")))
        .Expand()
        .BorderColor(Color.Grey));

bool exit = false;
while (!exit)
{
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[yellow]What would you like to do?[/]")
            .PageSize(10)
            .AddChoices(new[] {
                "Run Graph Ingestion (WHO ICD)",
                "Search Diseases by Symptoms",
                "Clear Graph Data [red](Destructive)[/]",
                "Exit"
            }));

    switch (choice)
    {
        case "Run Graph Ingestion (WHO ICD)":
            await RunImportAsync(serviceProvider, rootIcdUri);
            break;
        case "Search Diseases by Symptoms":
            await RunSearchAsync(serviceProvider);
            break;
        case "Clear Graph Data [red](Destructive)[/]":
            await ClearDatabaseAsync(serviceProvider);
            break;
        case "Exit":
            exit = true;
            break;
    }
}

static async Task RunImportAsync(IServiceProvider sp, string rootUri)
{
    await AnsiConsole.Status()
        .StartAsync("Initializing Graph Schema...", async ctx => 
        {
            var repository = sp.GetRequiredService<INeo4jRepository>();
            await repository.CreateConstraintsAsync();
            ctx.Status("Schema Constraints Applied.");
            await Task.Delay(1000);
        });

    var importer = sp.GetRequiredService<IcdImportService>();

    await AnsiConsole.Progress()
        .Columns(new ProgressColumn[] 
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new RemainingTimeColumn(),
            new SpinnerColumn(),
        })
        .StartAsync(async ctx => 
        {
            var task = ctx.AddTask("[green]Ingesting Medical Knowledge...[/]");
            
            await importer.ImportConceptHierarchyAsync(rootUri, (progress, total, queue) => 
            {
                task.Value = progress;
                task.Description = $"[green]Processed:[/] {total} diseases/symptoms (Queue: {queue})";
            });

            task.Value = 100;
            task.StopTask();
        });

    AnsiConsole.MarkupLine("[bold green]✔[/] Ingestion completed successfully!");
    AnsiConsole.WriteLine();
}

static async Task RunSearchAsync(IServiceProvider sp)
{
    var input = AnsiConsole.Ask<string>("[yellow]Enter symptoms (comma-separated):[/]");
    var normalizationService = sp.GetRequiredService<ISymptomNormalizationService>();
    var repository = sp.GetRequiredService<INeo4jRepository>();

    var rawSymptoms = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var normalizedSymptoms = rawSymptoms.Select(normalizationService.Normalize).Where(s => !string.IsNullOrEmpty(s)).ToList();

    if (!normalizedSymptoms.Any())
    {
        AnsiConsole.MarkupLine("[red]No valid symptoms entered.[/]");
        return;
    }

    AnsiConsole.MarkupLine($"[grey]Searching for:[/] {string.Join(", ", normalizedSymptoms)}...");

    var results = await repository.SearchDiseasesBySymptomsAsync(normalizedSymptoms);

    if (!results.Any())
    {
        AnsiConsole.MarkupLine("[orange1]No matching diseases found for these symptoms.[/]");
    }
    else
    {
        var table = new Table();
        table.AddColumn("[bold blue]Disease[/]");
        table.AddColumn("[bold cyan]ICD Code[/]");
        table.AddColumn("[bold green]Match Score[/]");

        foreach (var res in results)
        {
            table.AddRow(res.Title, res.IcdCode, $"{res.Score:P1}");
        }

        AnsiConsole.Write(table);
    }
    
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Press Enter to continue...[/]");
    Console.ReadLine();
}

static async Task ClearDatabaseAsync(IServiceProvider sp)
{
    if (!AnsiConsole.Confirm("[red]Are you sure you want to delete ALL graph data?[/]", false))
    {
        return;
    }

    await AnsiConsole.Status()
        .StartAsync("Clearing Graph Data...", async ctx => 
        {
            var repository = sp.GetRequiredService<INeo4jRepository>();
            await repository.ClearDatabaseAsync();
            ctx.Status("Graph Cleared.");
            await Task.Delay(1000);
        });

    AnsiConsole.MarkupLine("[bold red]✔[/] All graph data cleared successfully!");
    AnsiConsole.WriteLine();
}

if (serviceProvider is IAsyncDisposable disposable)
    await disposable.DisposeAsync();

AnsiConsole.MarkupLine("[grey]Goodbye![/]");
