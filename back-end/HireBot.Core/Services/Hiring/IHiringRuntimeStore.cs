namespace HireBot.Core.Services.Hiring;

internal interface IHiringRuntimeStore
{
    HiringRuntimeContext? Get(string hireId);
    HiringRuntimeContext? GetBySessionId(string sessionId);
    void Upsert(HiringRuntimeContext context);
}
