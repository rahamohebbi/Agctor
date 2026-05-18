using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Runtime;

/// <summary>Documents which actor runtimes are production-ready vs experimental.</summary>
public static class AgctorRuntimeCatalog
{
    public const string InMemory = "InMemory";
    public const string ProtoActor = "Proto.Actor";
    public const string Orleans = "Orleans";

    private static readonly HashSet<string> Experimental = new(StringComparer.OrdinalIgnoreCase)
    {
        ProtoActor, "Proto", Orleans
    };

    public static bool IsExperimental(string runtimeName) =>
        !string.IsNullOrWhiteSpace(runtimeName) && Experimental.Contains(runtimeName.Trim());

    public static string NormalizeRuntimeName(string? runtimeName) =>
        runtimeName?.Trim() switch
        {
            "Proto" => ProtoActor,
            null or "" => InMemory,
            var n => n
        };
}
