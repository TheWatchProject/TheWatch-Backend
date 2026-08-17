using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Persistence;

public interface ISqlServerPort<TEntity> : IDatabasePort<TEntity> where TEntity : class
{
    Task<List<TEntity>> ExecuteStoredProcedureAsync(string spName, IDictionary<string, object> parameters, CancellationToken ct = default);
    Task<int> ExecuteCommandParameterizedAsync(string sql, object[] parameters, CancellationToken ct = default);
}

public class SqlServerDatabaseAdapter<TEntity, TDbContext> : ISqlServerPort<TEntity>
    where TEntity : class
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly ILogger<SqlServerDatabaseAdapter<TEntity, TDbContext>> _logger;

    public SqlServerDatabaseAdapter(TDbContext dbContext, ILogger<SqlServerDatabaseAdapter<TEntity, TDbContext>> logger)
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

    public async Task<List<TEntity>> ExecuteStoredProcedureAsync(string spName, IDictionary<string, object> parameters, CancellationToken ct = default)
    {
        _logger.LogInformation("Executing SQL Server Stored Procedure {SpName}", spName);
        var paramStrings = string.Join(", ", parameters.Keys.Select(k => $"@{k} = @{k}"));
        var rawSql = $"EXEC {spName} {paramStrings}";
        var paramValues = parameters.Values.ToArray();

        return await _dbContext.Set<TEntity>().FromSqlRaw(rawSql, paramValues).ToListAsync(ct);
    }

    public async Task<int> ExecuteCommandParameterizedAsync(string sql, object[] parameters, CancellationToken ct = default)
    {
        return await _dbContext.Database.ExecuteSqlRawAsync(sql, parameters, ct);
    }
}
