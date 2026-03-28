using Neo4j.Driver;
using BoAliSina.IcdImporter.Models.Graph;

namespace BoAliSina.IcdImporter.Repositories;

public interface INeo4jRepository : IAsyncDisposable
{
    Task CreateConstraintsAsync();
    Task MergeDiseaseWithSymptomsAsync(DiseaseNode disease, IEnumerable<SymptomNode> symptoms);
    Task<IEnumerable<DiseaseSearchResult>> SearchDiseasesBySymptomsAsync(IEnumerable<string> symptoms);
    Task ClearDatabaseAsync();
}

public record DiseaseSearchResult(string Title, string IcdCode, double Score);

public class Neo4jRepository(string uri, string user, string password) : INeo4jRepository
{
    private readonly IDriver _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

    public async Task CreateConstraintsAsync()
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("CREATE CONSTRAINT disease_id IF NOT EXISTS FOR (d:Disease) REQUIRE d.id IS UNIQUE");
            await tx.RunAsync("CREATE CONSTRAINT symptom_name IF NOT EXISTS FOR (s:Symptom) REQUIRE s.normalizedName IS UNIQUE");
            await tx.RunAsync("CREATE INDEX disease_title IF NOT EXISTS FOR (d:Disease) ON (d.title)");
        });
    }

    public async Task MergeDiseaseWithSymptomsAsync(DiseaseNode disease, IEnumerable<SymptomNode> symptoms)
    {
        await using var session = _driver.AsyncSession();
        const string query = @"
            MERGE (d:Disease {id: $id})
            SET d.icdCode = $icdCode,
                d.title = $title,
                d.description = $description,
                d.source = $source,
                d.updatedAt = datetime()
            
            WITH d
            UNWIND $symptoms AS sym
            MERGE (s:Symptom {normalizedName: sym.normalizedName})
            SET s.displayName = sym.displayName,
                s.source = sym.source
            MERGE (d)-[:HAS_SYMPTOM]->(s)";

        var parameters = new Dictionary<string, object?>
        {
            ["id"] = disease.Id,
            ["icdCode"] = disease.IcdCode,
            ["title"] = disease.Title,
            ["description"] = disease.Description,
            ["source"] = disease.Source,
            ["symptoms"] = symptoms.Select(s => new Dictionary<string, object>
            {
                ["normalizedName"] = s.NormalizedName,
                ["displayName"] = s.DisplayName,
                ["source"] = s.Source
            }).ToList()
        };

        await session.ExecuteWriteAsync(async tx => 
        {
            await tx.RunAsync(query, parameters);
        });
    }

    public async Task<IEnumerable<DiseaseSearchResult>> SearchDiseasesBySymptomsAsync(IEnumerable<string> symptoms)
    {
        await using var session = _driver.AsyncSession();
        // Simple overlap-based score: number of matching symptoms / total symptoms of the disease
        const string query = @"
            MATCH (s:Symptom)
            WHERE s.normalizedName IN $symptoms
            MATCH (d:Disease)-[:HAS_SYMPTOM]->(s)
            WITH d, count(s) AS matchedCount
            MATCH (d)-[:HAS_SYMPTOM]->(totalS:Symptom)
            WITH d, matchedCount, count(totalS) AS totalCount
            RETURN d.title AS Title, d.icdCode AS IcdCode, 
                   (toFloat(matchedCount) / toFloat(totalCount)) AS Score
            ORDER BY Score DESC, matchedCount DESC
            LIMIT 10";

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(query, new { symptoms = symptoms.ToArray() });
            return await cursor.ToListAsync(record => new DiseaseSearchResult(
                record["Title"].As<string>(),
                record["IcdCode"].As<string>(),
                record["Score"].As<double>()
            ));
        });

        return result;
    }

    public async Task ClearDatabaseAsync()
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(tx => tx.RunAsync("MATCH (n) DETACH DELETE n"));
    }

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
