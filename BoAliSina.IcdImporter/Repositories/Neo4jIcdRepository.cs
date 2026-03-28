using Neo4j.Driver;
using BoAliSina.IcdImporter.Models.Graph;

namespace BoAliSina.IcdImporter.Repositories;

public interface IIcdGraphRepository : IAsyncDisposable
{
    Task CreateConstraintsAsync();
    Task MergeConceptsBatchAsync(IEnumerable<IcdConceptNode> nodes);
    Task MergeRelationshipsBatchAsync(IEnumerable<IcdRelationship> relationships);
    Task MergeSideEffectsBatchAsync(IEnumerable<SideEffectNode> nodes);
    Task LinkSideEffectsBatchAsync(IEnumerable<SideEffectRelationship> relationships);
    Task ClearDatabaseAsync();
}

public class Neo4jIcdRepository : IIcdGraphRepository
{
    private readonly IDriver _driver;

    public Neo4jIcdRepository(string uri, string user, string password)
    {
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
    }

    public async Task CreateConstraintsAsync()
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("CREATE CONSTRAINT icd_concept_uri IF NOT EXISTS FOR (c:ICDConcept) REQUIRE c.uri IS UNIQUE");
            await tx.RunAsync("CREATE INDEX icd_concept_code IF NOT EXISTS FOR (c:ICDConcept) ON (c.code)");
            await tx.RunAsync("CREATE CONSTRAINT side_effect_name IF NOT EXISTS FOR (s:SideEffect) REQUIRE s.name IS UNIQUE");
        });
    }

    public async Task MergeConceptsBatchAsync(IEnumerable<IcdConceptNode> nodes)
    {
        await using var session = _driver.AsyncSession();
        const string query = @"
            UNWIND $batch AS item
            MERGE (c:ICDConcept {uri: item.Uri})
            SET c.code = item.Code,
                c.title = item.Title,
                c.definition = item.Definition,
                c.classKind = item.ClassKind,
                c.language = item.Language,
                c.updatedAt = datetime()";

        await session.ExecuteWriteAsync(async tx =>
            await tx.RunAsync(query, new { batch = nodes.Select(n => new { 
                n.Uri, n.Code, n.Title, n.Definition, n.ClassKind, n.Language 
            }) }));
    }

    public async Task MergeRelationshipsBatchAsync(IEnumerable<IcdRelationship> relationships)
    {
        await using var session = _driver.AsyncSession();
        const string query = @"
            UNWIND $batch AS rel
            MATCH (child:ICDConcept {uri: rel.ChildUri})
            MATCH (parent:ICDConcept {uri: rel.ParentUri})
            MERGE (child)-[:HAS_PARENT]->(parent)";

        await session.ExecuteWriteAsync(async tx =>
            await tx.RunAsync(query, new { batch = relationships.Select(r => new { 
                r.ChildUri, r.ParentUri 
            }) }));
    }

    public async Task MergeSideEffectsBatchAsync(IEnumerable<SideEffectNode> nodes)
    {
        await using var session = _driver.AsyncSession();
        const string query = @"
            UNWIND $batch AS item
            MERGE (s:SideEffect {name: item.Name})
            SET s.description = item.Description,
                s.updatedAt = datetime()";

        await session.ExecuteWriteAsync(async tx =>
            await tx.RunAsync(query, new { batch = nodes.Select(n => new { 
                n.Name, n.Description 
            }) }));
    }

    public async Task LinkSideEffectsBatchAsync(IEnumerable<SideEffectRelationship> relationships)
    {
        await using var session = _driver.AsyncSession();
        const string query = @"
            UNWIND $batch AS rel
            MATCH (c:ICDConcept {uri: rel.ConceptUri})
            MATCH (s:SideEffect {name: rel.SideEffectName})
            MERGE (c)-[:HAS_SIDE_EFFECT]->(s)";

        await session.ExecuteWriteAsync(async tx =>
            await tx.RunAsync(query, new { batch = relationships.Select(r => new { 
                r.ConceptUri, r.SideEffectName 
            }) }));
    }

    public async Task ClearDatabaseAsync()
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
            await tx.RunAsync("MATCH (n) DETACH DELETE n"));
    }

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
