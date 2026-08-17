using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Persistence;

public class GenericEfCoreAdapter<TEntity, TDbContext> : IDatabasePort<TEntity> 
    where TEntity : class 
    where TDbContext : DbContext
{
    private readonly TDbContext _context;

    public GenericEfCoreAdapter(TDbContext context)
    {
        _context = context;
    }

    public async Task<TEntity?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await _context.Set<TEntity>().FindAsync(new object[] { id }, ct);
    }

    public async Task<List<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)
    {
        if (predicate == null)
            return await _context.Set<TEntity>().ToListAsync(ct);
        return await _context.Set<TEntity>().Where(predicate).ToListAsync(ct);
    }

    public async Task AddAsync(TEntity entity, CancellationToken ct = default)
    {
        await _context.Set<TEntity>().AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        _context.Set<TEntity>().Update(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity != null)
        {
            _context.Set<TEntity>().Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }
}