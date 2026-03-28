namespace BoAliSina.IcdImporter.Models.Graph;

public record DiseaseNode(
    string Id,
    string IcdCode,
    string Title,
    string? Description,
    string Source = "WHO_ICD_11"
);
