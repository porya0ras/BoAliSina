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
services.AddSingleton<IIcdGraphRepository>(new Neo4jIcdRepository(neo4jUri, neo4jUser, neo4jPass));
services.AddScoped<IcdImportService>();

var serviceProvider = services.BuildServiceProvider();

// --- UI Logic ---
AnsiConsole.Clear();
AnsiConsole.Write(
    new FigletText("BoAliSina")
        .Centered()
        .Color(Color.Purple));

AnsiConsole.Write(
    new Panel(Align.Center(new Markup("[bold white]WHO ICD-11 to Neo4j Importer[/]\n[grey]Version 1.0.0[/]")))
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
                "Run Full Import",
                "Add Sample Side Effects",
                "Exit"
            }));

    switch (choice)
    {
        case "Run Full Import":
            await RunImportAsync(serviceProvider, rootIcdUri);
            break;
        case "Add Sample Side Effects":
            await AddSampleDataAsync(serviceProvider, rootIcdUri);
            break;
        case "Exit":
            exit = true;
            break;
    }
}

static async Task RunImportAsync(IServiceProvider sp, string rootUri)
{
    await AnsiConsole.Status()
        .StartAsync("Connecting to Database...", async ctx => 
        {
            var repository = sp.GetRequiredService<IIcdGraphRepository>();
            await repository.CreateConstraintsAsync();
            ctx.Status("Database Ready.");
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
            var task = ctx.AddTask("[green]Importing ICD Hierarchy[/]");
            
            await importer.ImportConceptHierarchyAsync(rootUri, (progress, total, queue) => 
            {
                task.Value = progress;
                task.Description = $"[green]Importing:[/] {total} concepts (Queue: {queue})";
            });

            task.Value = 100;
            task.StopTask();
        });

    AnsiConsole.MarkupLine("[bold green]✔[/] Import completed successfully!");
    AnsiConsole.WriteLine();
}

static async Task AddSampleDataAsync(IServiceProvider sp, string rootUri)
{
    await AnsiConsole.Status()
        .StartAsync("Adding Sample Data...", async ctx => 
        {
            var repository = sp.GetRequiredService<IIcdGraphRepository>();
            
            var sideEffects = new[] { 
                new SideEffectNode("Fever", "Elevated body temperature"),
                new SideEffectNode("Cough", "A sudden, forceful expulsion of air from the lungs")
            };
            await repository.MergeSideEffectsBatchAsync(sideEffects);

            var links = new[] {
                new SideEffectRelationship(rootUri, "Fever")
            };
            await repository.LinkSideEffectsBatchAsync(links);

            ctx.Status("Sample Data Added.");
            await Task.Delay(1000);
        });

    AnsiConsole.MarkupLine("[bold blue]i[/] Side Effect demo data added.");
}

if (serviceProvider is IAsyncDisposable disposable)
    await disposable.DisposeAsync();

AnsiConsole.MarkupLine("[grey]Goodbye![/]");
