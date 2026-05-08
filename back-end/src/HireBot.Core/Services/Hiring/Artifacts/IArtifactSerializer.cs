using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Core.Services.Hiring.Artifacts;

internal interface IArtifactSerializer
{
    ArtifactSerializationResult Serialize(ArtifactSerializationRequest request);
}
