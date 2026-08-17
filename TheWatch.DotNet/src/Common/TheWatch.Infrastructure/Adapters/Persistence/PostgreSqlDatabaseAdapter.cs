using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Persistence;

public interface IPostgreSqlPort<TEntity> : IDatabasePort<TEntity> where TEntity : class
{
    Task<List<TEntity>> QueryNearbyEntitiesAsync(double lat, double lng, double radiusMeters, CancellationToken ct = default);
    Task<List<TEntity>> QueryJsonbFieldAsync(string jsonbPath, string value, CancellationToken ct = default);
}

public class PostgreSqlDatabaseAdapter<TEntity, TDbContext> : IPostgreSqlPort<TEntity>
    where TEntity : class
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly ILogger<PostgreSqlDatabaseAdapter<TEntity, TDbContext>> _logger;

    public PostgreSqlDatabaseAdapter(TDbContext dbContext, ILogger<PostgreSqlDatabaseAdapter<TEntity, TDbContext>> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<TEntity?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await _dbContext.Set<TEntity>().FindAsync(new object[] { id }, ct);
    }

    public async Task<List<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = _dbContext.Set<TEntity>().AsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await query.ToListAsync(ct);
    }

    public async Task AddAsync(TEntity entity, CancellationToken ct = default)
    {
        await _dbContext.Set<TEntity>().AddAsync(entity, ct);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        _dbContext.Set<TEntity>().Update(entity);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity != null)
        {
            _dbContext.Set<TEntity>().Remove(entity);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<List<TEntity>> QueryNearbyEntitiesAsync(double lat, double lng, double radiusMeters, CancellationToken ct = default)
    {
        _logger.LogInformation("Executing PostGIS geospatial distance filter: ({Lat}, {Lng}) with radius {Radius}m", lat, lng, radiusMeters);
        var rawSql = "SELECT * FROM \"Incidents\" WHERE ST_DWithin(Location, ST_SetSRID(ST_MakePoint({0}, {1}), 4326)::geography, {2})";
        return await _dbContext.Set<TEntity>().FromSqlRaw(rawSql, lng, lat, radiusMeters).ToListAsync(ct);
    }

    public async Task<List<TEntity>> QueryJsonbFieldAsync(string jsonbPath, string value, CancellationToken ct = default)
    {
        var rawSql = $"SELECT * FROM \"TelemetryEvents\" WHERE Data #>> '{{{jsonbPath}}}' = {{0}}";
        return await _dbContext.Set<TEntity>().FromSqlRaw(rawSql, value).ToListAsync(ct);
    }
}
