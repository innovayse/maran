namespace Maran.Host.OpenApi;

/// <summary>The fixed names of the panel's one OpenAPI document.</summary>
public static class OpenApiDocumentNames
{
    /// <summary>The generator's name for the document: the API's major version, as its routes spell it.</summary>
    public const string Document = "v1";

    /// <summary>The title in the document's <c>info</c> block.</summary>
    public const string Title = "Maran panel API";

    /// <summary>The name of the bearer-token security scheme.</summary>
    public const string BearerScheme = "Bearer";
}
