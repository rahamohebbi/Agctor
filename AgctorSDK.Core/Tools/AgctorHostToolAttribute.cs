using System;

namespace AgctorSDK.Core.Tools;

/// <summary>
/// Marks an <see cref="IToolActor"/> for host discovery: HTTP id, dashboard metadata, and factory registration.
/// Types without this attribute are not exposed unless added manually to the catalog.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgctorHostToolAttribute : Attribute
{
    public AgctorHostToolAttribute(string httpId, string displayName, string description)
    {
        HttpId = httpId;
        DisplayName = displayName;
        Description = description;
    }

    /// <summary>Stable REST id (e.g. <c>person-memory-context</c>).</summary>
    public string HttpId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    /// <summary>When true, <c>GET /api/tools</c> and ToolInvoker accept this id.</summary>
    public bool ExposeOnHttpApi { get; set; } = true;

    /// <summary>Used when HTTP invoke omits <c>operation</c> (passthrough to <see cref="Models.ToolRequest"/>).</summary>
    public string DefaultOperation { get; set; } = "";
}
