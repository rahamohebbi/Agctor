using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Models;

public sealed class RoutingRulesSchema
{
    public List<RoutingRule> RoutingRules { get; set; } = new();
}

public sealed class RoutingRule
{
    public RoutingWhen When { get; set; } = new();
    public RoutingTarget Target { get; set; } = new();
}

public sealed class RoutingWhen
{
    public string KnowledgeType { get; set; } = "";
    public string? Attribute { get; set; }
}

public sealed class RoutingTarget
{
    public string Document { get; set; } = "";
    public string Section { get; set; } = "";
}
