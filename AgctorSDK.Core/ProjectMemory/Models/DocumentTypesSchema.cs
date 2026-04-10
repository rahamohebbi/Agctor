using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Models;

public sealed class DocumentTypesSchema
{
    public List<DocumentTypeDef> DocumentTypes { get; set; } = new();
}

public sealed class DocumentTypeDef
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string UpdateMode { get; set; } = "replace_section";
    public List<string> Sections { get; set; } = new();
}
