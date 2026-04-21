using System.Collections.Generic;
using System.Threading.Tasks;

namespace HireBot.Abstraction;

public interface IHireBotRepository
{
    // 通用仓储方法
    Task<T?> GetByIdAsync<T>(int id) where T : class;
    Task<IEnumerable<T>> GetAllAsync<T>() where T : class;
    Task<T> AddAsync<T>(T entity) where T : class;
    Task<T> UpdateAsync<T>(T entity) where T : class;
    Task DeleteAsync<T>(int id) where T : class;
}