using System.Text.Json.Serialization;

namespace BoAliSina.IcdImporter.Models.Api;

public record IcdApiConceptDto(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] LanguagedTextDto Title,
    [property: JsonPropertyName("definition")] LanguagedTextDto? Definition,
    [property: JsonPropertyName("classKind")] string ClassKind,
    [property: JsonPropertyName("parent")] string[]? Parent,
    [property: JsonPropertyName("child")] string[]? Child,
    [property: JsonPropertyName("synonym")] TermDto[]? Synonym,
    [property: JsonPropertyName("inclusion")] TermDto[]? Inclusion,
    [property: JsonPropertyName("exclusion")] TermDto[]? Exclusion
);

public record LanguagedTextDto(
    [property: JsonPropertyName("@language")] string Language,
    [property: JsonPropertyName("@value")] string Value
);

public record TermDto(
    [property: JsonPropertyName("label")] LanguagedTextDto Label
);
