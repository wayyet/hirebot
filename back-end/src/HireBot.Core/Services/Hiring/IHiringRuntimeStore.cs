namespace HireBot.Core.Services.Hiring;

internal interface IHiringRuntimeStore
{
    HiringRuntimeContext? Get(string hireId);
    void Upsert(HiringRuntimeContext context);
}
