using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>Dashboard: persona-scoped tool catalog for Agent Studio and flow designer.</summary>
public interface IPersonaHostToolsService
{
    Task<PersonaHostToolsResponseDto> GetForPersonaAsync(string personaId, CancellationToken cancellationToken = default);
}
