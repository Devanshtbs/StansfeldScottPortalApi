using Microsoft.EntityFrameworkCore;
using StansfeldScott.Portal.Domain.Entities;
using StansfeldScott.Portal.Infrastructure.Data;
using StansfeldScott.Portal.Repositories.Interfaces;

namespace StansfeldScott.Portal.Repositories.Base;


public class Repository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<T> DbSet;

    public Repository(AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id) =>
        await DbSet.FindAsync(id);

    public async Task<IReadOnlyList<T>> GetAllAsync() =>
        await DbSet.AsNoTracking().ToListAsync();

    public async Task<T> AddAsync(T entity)
    {
        await DbSet.AddAsync(entity);
        await Context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(T entity)
    {
        entity.UpdatedAtUtc = DateTime.UtcNow;
        DbSet.Update(entity);
        await Context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await DbSet.FindAsync(id);
        if (entity is null) return;
        DbSet.Remove(entity);
        await Context.SaveChangesAsync();
    }
}
