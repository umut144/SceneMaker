using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneMaker.Core;

public static class DocumentJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    static DocumentJson()
    {
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }

    public static string Serialize(WorkspaceDocument document)
    {
        DocumentValidation.Validate(document);
        return JsonSerializer.Serialize(document, Options) + "\n";
    }

    public static string Serialize(SceneDocument document)
    {
        DocumentValidation.Validate(document);
        return JsonSerializer.Serialize(document, Options) + "\n";
    }

    public static WorkspaceDocument DeserializeWorkspace(string json)
    {
        var document = Deserialize<WorkspaceDocument>(json, "Workspace");
        DocumentValidation.Validate(document);
        return document;
    }

    public static SceneDocument DeserializeScene(string json)
    {
        var document = Deserialize<SceneDocument>(json, "Scene");
        DocumentValidation.Validate(document);
        return document;
    }

    private static T Deserialize<T>(string json, string label)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new SceneMakerDocumentException($"{label} document must not contain JSON null.");
        }
        catch (SceneMakerDocumentException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SceneMakerDocumentException(
                $"{label} document is not valid SceneMaker JSON: {exception.Message}", exception);
        }
    }
}
