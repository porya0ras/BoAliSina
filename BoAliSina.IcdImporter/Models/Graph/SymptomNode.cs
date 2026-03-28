namespace BoAliSina.IcdImporter.Models.Graph;

public record SymptomNode(
    string NormalizedName,
    string DisplayName,
    string Source = "WHO_ICD_11"
);
