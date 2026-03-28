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
        var queue = new Queue<string>();
        queue.Enqueue(rootUri);

            DateTime lastProgressUpdate = DateTime.MinValue;

            while (queue.Count > 0)
            {
                var uri = queue.Dequeue();
                if (visited.Contains(uri)) continue;

                var dto = await _apiClient.GetConceptAsync(uri);
                if (dto != null)
                {
                    await ProcessConceptAsync(dto);
                    visited.Add(uri);

                    if (dto.Child != null)
                    {
                        foreach (var child in dto.Child)
                        {
                            if (!visited.Contains(child)) queue.Enqueue(child);
                        }
                    }
                }

                if (DateTime.UtcNow - lastProgressUpdate > TimeSpan.FromMilliseconds(200))
                {
                    double progress = visited.Count / (double)(visited.Count + queue.Count) * 100;
                    onProgress?.Invoke(progress, visited.Count, queue.Count);
                    lastProgressUpdate = DateTime.UtcNow;
                }
            }

            // Final progress update
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
