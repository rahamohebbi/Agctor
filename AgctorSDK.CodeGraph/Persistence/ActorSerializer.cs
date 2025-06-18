using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;

namespace AgctorSDK.CodeGraph.Persistence
{
    /// <summary>
    /// Static helper that converts <see cref="CodeGraphActorBase"/> hierarchies to/from DTOs that can be JSON-serialized.
    /// Stage-1 keeps things simple: a single JSON file representing the full tree.
    /// </summary>
    internal static class ActorSerializer
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        #region Public API
        public static async Task WriteAsync(ActorDto dto, string filePath)
        {
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, dto, _options);
        }

        public static async Task<ActorDto> ReadAsync(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            var dto = await JsonSerializer.DeserializeAsync<ActorDto>(stream, _options);
            return dto ?? throw new InvalidDataException($"Unable to deserialize actor data from {filePath}");
        }

        public static ActorDto ToDto(CodeGraphActorBase actor)
        {
            var children = new List<ActorDto>();
            foreach (var child in actor.Children)
            {
                children.Add(ToDto(child));
            }

            return new ActorDto
            {
                Id = actor.Id,
                ActorType = actor.ActorType,
                Name = actor.Name,
                PhysicalPath = actor.PhysicalPath,
                Children = children
            };
        }

        public static CodeGraphActorBase FromDto(ActorDto dto)
        {
            CodeGraphActorBase actor = dto.ActorType switch
            {
                nameof(SolutionActor) => new SolutionActor(dto.Name, dto.PhysicalPath ?? string.Empty),
                nameof(ProjectActor) => new ProjectActor(dto.Name, dto.PhysicalPath ?? string.Empty),
                nameof(FileActor) => new FileActor(dto.Name, dto.PhysicalPath ?? string.Empty),
                nameof(ClassActor) => new ClassActor(dto.Name),
                nameof(MethodActor) => new MethodActor(dto.Name),
                _ => throw new InvalidDataException($"Unknown actor type: {dto.ActorType}")
            };

            if (dto.Children != null)
            {
                foreach (var childDto in dto.Children)
                {
                    var childActor = FromDto(childDto);
                    actor.AddChild(childActor);
                }
            }

            return actor;
        }
        #endregion

        #region DTO
        public class ActorDto
        {
            public string Id { get; set; } = string.Empty;
            public string ActorType { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? PhysicalPath { get; set; }
            public List<ActorDto>? Children { get; set; }
        }
        #endregion
    }
} 