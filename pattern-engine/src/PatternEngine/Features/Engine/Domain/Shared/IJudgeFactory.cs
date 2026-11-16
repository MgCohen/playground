using PatternEngine.Features.Packs.Domain;
using PatternEngine.Plugins.Agents;

namespace PatternEngine.Features.Engine.Domain.Shared;

/// <summary>Makes the judge for one pack and one model. The host decides the kind (settings judge.kind); tests give a scripted judge.</summary>
public interface IJudgeFactory
{
    IJudge Create(PackContext pack, string? model);
}
