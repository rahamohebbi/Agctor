using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgctorSDK.Core.ProjectMemory.Yaml;

/// <summary>
/// Shared YAML (de)serialization for portable .agctor manifests (camelCase keys in files).
/// </summary>
public static class ProjectYamlSerializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static T Deserialize<T>(string yaml)
    {
        return Deserializer.Deserialize<T>(yaml);
    }

    public static T DeserializeFromFile<T>(string path)
    {
        var yaml = File.ReadAllText(path);
        return Deserialize<T>(yaml);
    }

    public static string Serialize<T>(T value)
    {
        return Serializer.Serialize(value);
    }
}
