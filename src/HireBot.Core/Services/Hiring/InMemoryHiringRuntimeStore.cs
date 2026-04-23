using System.Collections.Concurrent;

namespace HireBot.Core.Services.Hiring;

internal sealed class InMemoryHiringRuntimeStore : IHiringRuntimeStore
{
    private readonly ConcurrentDictionary<string, HiringRuntimeContext> contexts = new(StringComparer.OrdinalIgnoreCase);

    public HiringRuntimeContext? Get(string hireId)
    {
        return contexts.TryGetValue(hireId, out var context) ? context : null;
    }

    public void Upsert(HiringRuntimeContext context)
    {
        contexts[context.HireId] = context;
    }
}
