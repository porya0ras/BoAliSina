using BoAliSina.IcdImporter.Models.Api;
using BoAliSina.IcdImporter.Models.Graph;
using BoAliSina.IcdImporter.Repositories;

namespace BoAliSina.IcdImporter.Services;

public class IcdImportService
{
    private readonly IIcdApiClient _apiClient;
    private readonly IIcdGraphRepository _repository;

    public IcdImportService(IIcdApiClient apiClient, IIcdGraphRepository repository)
    {
        _apiClient = apiClient;
        _repository = repository;
    }

    public async Task ImportConceptHierarchyAsync(string rootUri)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(rootUri);

        while (queue.Count > 0)
        {
            var currentBatch = new List<IcdApiConceptDto>();
            
            // Process in batches of 50 for API calls (to avoid overwhelming)
            for (int i = 0; i < 50 && queue.Count > 0; i++)
            {
                var uri = queue.Dequeue();
                if (visited.Contains(uri)) continue;

                var dto = await _apiClient.GetConceptAsync(uri);
                if (dto != null)
                {
                    currentBatch.Add(dto);
                    visited.Add(uri);

                    if (dto.Child != null)
                    {
                        foreach (var child in dto.Child)
                        {
                            if (!visited.Contains(child)) queue.Enqueue(child);
                        }
                    }
                }
            }

            if (currentBatch.Any())
            {
                await SaveBatchToGraphAsync(currentBatch);
            }
        }
    }

    private async Task SaveBatchToGraphAsync(List<IcdApiConceptDto> batch)
    {
        var nodes = batch.Select(dto => new IcdConceptNode(
            Uri: dto.Id,
            Code: dto.Code,
            Title: dto.Title.Value,
            Definition: dto.Definition?.Value,
            ClassKind: dto.ClassKind,
            Language: dto.Title.Language
        ));

        var rels = batch
            .Where(dto => dto.Parent != null)
            .SelectMany(dto => dto.Parent!.Select(p => new IcdRelationship(dto.Id, p)));

        await _repository.MergeConceptsBatchAsync(nodes);
        await _repository.MergeRelationshipsBatchAsync(rels);
    }
}
