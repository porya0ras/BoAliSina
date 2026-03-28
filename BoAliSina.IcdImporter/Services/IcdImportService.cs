using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BoAliSina.IcdImporter.Models.Api;
using BoAliSina.IcdImporter.Models.Graph;
using BoAliSina.IcdImporter.Repositories;

namespace BoAliSina.IcdImporter.Services;

public class IcdImportService
{
    private readonly IIcdApiClient _apiClient;
    private readonly INeo4jRepository _repository;
    private readonly ISymptomNormalizationService _normalizationService;

    public IcdImportService(
        IIcdApiClient apiClient, 
        INeo4jRepository repository, 
        ISymptomNormalizationService normalizationService)
    {
        _apiClient = apiClient;
        _repository = repository;
        _normalizationService = normalizationService;
    }

    public async Task ImportConceptHierarchyAsync(string rootUri, Action<double, int, int>? onProgress = null)
    {
        var visited = new HashSet<string>();
        var enqueued = new HashSet<string> { rootUri };
        var queue = new ConcurrentQueue<string>();
        queue.Enqueue(rootUri);

        var lastProgressUpdate = DateTime.MinValue;
        var semaphore = new SemaphoreSlim(10); // Faster parallelism
        var tasks = new ConcurrentBag<Task>();

        while (!queue.IsEmpty || tasks.Any(t => !t.IsCompleted))
        {
            if (queue.TryDequeue(out var uri))
            {
                await semaphore.WaitAsync();
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var dto = await _apiClient.GetConceptAsync(uri);
                        if (dto != null)
                        {
                            await ProcessConceptAsync(dto);
                            lock (visited) { visited.Add(uri); }

                            if (dto.Child != null)
                            {
                                foreach (var child in dto.Child)
                                {
                                    lock (enqueued)
                                    {
                                        if (enqueued.Add(child)) queue.Enqueue(child);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Log locally or handle if necessary
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                tasks.Add(task);
            }
            else
            {
                await Task.Delay(50);
            }

            // Cleanup periodically to avoid memory overhead of finished tasks
            if (tasks.Count > 200)
            {
                var remaining = tasks.Where(t => !t.IsCompleted).ToList();
                tasks = new ConcurrentBag<Task>(remaining);
            }

            if (DateTime.UtcNow - lastProgressUpdate > TimeSpan.FromMilliseconds(200))
            {
                int visitedCount, queueCount;
                lock (visited) { visitedCount = visited.Count; }
                queueCount = queue.Count;

                int totalCount = visitedCount + queueCount;
                double progress = totalCount > 0 ? (visitedCount / (double)totalCount) * 100 : 0;
                
                onProgress?.Invoke(progress, visitedCount, queueCount);
                lastProgressUpdate = DateTime.UtcNow;
            }
        }
        
        await Task.WhenAll(tasks);
        onProgress?.Invoke(100, visited.Count, 0);
    }

    private async Task ProcessConceptAsync(IcdApiConceptDto dto)
    {
        // 1. Prepare Disease Node
        var disease = new DiseaseNode(
            Id: dto.Id,
            IcdCode: dto.Code,
            Title: dto.Title.Value,
            Description: dto.Definition?.Value
        );

        // 2. Prepare Symptom Nodes
        var symptomCandidates = ExtractSymptomCandidates(dto);
        var symptoms = new List<SymptomNode>();
        foreach (var candidate in symptomCandidates)
        {
            var normalizedName = _normalizationService.Normalize(candidate);
            if (string.IsNullOrWhiteSpace(normalizedName)) continue;

            symptoms.Add(new SymptomNode(
                NormalizedName: normalizedName,
                DisplayName: candidate
            ));
        }

        // 3. Batch Merge in a single transaction
        await _repository.MergeDiseaseWithSymptomsAsync(disease, symptoms);
    }

    private IEnumerable<string> ExtractSymptomCandidates(IcdApiConceptDto dto)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract from Synonyms
        if (dto.Synonym != null)
        {
            foreach (var s in dto.Synonym)
            {
                candidates.Add(s.Label.Value);
            }
        }

        // Extract from Inclusion
        if (dto.Inclusion != null)
        {
            foreach (var i in dto.Inclusion)
            {
                candidates.Add(i.Label.Value);
            }
        }

        // Potential logic for parsing Definition for keywords could go here
        // For now, synonyms and inclusions are the best sources of related clinical terms

        return candidates;
    }
}
