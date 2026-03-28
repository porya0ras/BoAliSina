namespace BoAliSina.IcdImporter.Models.Graph;

public record IcdConceptNode(
    string Uri,
    string Code,
    string Title,
    string? Definition,
    string ClassKind,
    string Language
);

public record IcdRelationship(
    string ChildUri,
    string ParentUri,
    string Type = "HAS_PARENT"
);

public record SideEffectNode(
    string Name,
    string? Description = null
);

public record SideEffectRelationship(
    string ConceptUri,
    string SideEffectName
);
