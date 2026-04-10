using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Models;

public sealed class EntityTypesSchema
{
    public List<EntityTypeDef> EntityTypes { get; set; } = new();
}

public sealed class EntityTypeDef
{
    public string Id { get; set; } = "";
    public string FolderPattern { get; set; } = "";
    public string MetadataFile { get; set; } = "entity.yaml";
    public string KeyStrategy { get; set; } = "slug_name";
    public List<string> RequiredDocuments { get; set; } = new();
    public List<string>? OptionalDocuments { get; set; }
}
