using HireBot.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Repository;

public sealed class HireBotRepository(HireBotDbContext context) : IHireBotRepository
{
    public async Task<T?> GetByIdAsync<T>(int id) where T : class
    {
        return await context.Set<T>().FindAsync(id);
    }

    public async Task<IEnumerable<T>> GetAllAsync<T>() where T : class
    {
        return await context.Set<T>().ToListAsync();
    }

    public async Task<T> AddAsync<T>(T entity) where T : class
    {
        var result = await context.Set<T>().AddAsync(entity);
        await context.SaveChangesAsync();
        return result.Entity;
    }

    public async Task<T> UpdateAsync<T>(T entity) where T : class
    {
        var result = context.Set<T>().Update(entity);
        await context.SaveChangesAsync();
        return result.Entity;
    }

    public async Task DeleteAsync<T>(int id) where T : class
    {
        var entity = await GetByIdAsync<T>(id);
        if (entity != null)
        {
            context.Set<T>().Remove(entity);
            await context.SaveChangesAsync();
        }
    }
}