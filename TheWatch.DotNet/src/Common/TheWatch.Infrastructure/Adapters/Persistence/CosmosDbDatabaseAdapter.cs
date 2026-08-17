using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using TheWatch.Application.Ports;

namespace TheWatch.Infrastructure.Adapters.Persistence;

public interface ICosmosDbPort<TEntity> : IDatabasePort<TEntity> where TEntity : class
{
    Task<TEntity?> GetWithPartitionKeyAsync(string id, string partitionKey, CancellationToken ct = default);
    Task<ItemResponse<TEntity>> UpsertItemAsync(TEntity entity, string partitionKey, CancellationToken ct = default);
    Task<List<TEntity>> QueryParameterizedAsync(QueryDefinition queryDefinition, string? partitionKey = null, CancellationToken ct = default);
}

public class CosmosDbDatabaseAdapter<TEntity> : ICosmosDbPort<TEntity> where TEntity : class
{
    private readonly Container _container;
    private readonly ILogger<CosmosDbDatabaseAdapter<TEntity>> _logger;

    public CosmosDbDatabaseAdapter(CosmosClient cosmosClient, string databaseName, string containerName, ILogger<CosmosDbDatabaseAdapter<TEntity>> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    public async Task<TEntity?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await GetWithPartitionKeyAsync(id, id, ct);
    }

    public async Task<TEntity?> GetWithPartitionKeyAsync(string id, string partitionKey, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<TEntity>(id, new PartitionKey(partitionKey), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Item {Id} not found in Cosmos container {Container}", id, _container.Id);
            return null;
        }
    }

    public async Task<List<TEntity>> ListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)
    {
        var queryable = _container.GetItemLinqQueryable<TEntity>();
        var feed = predicate == null ? queryable.ToFeedIterator() : queryable.Where(predicate).ToFeedIterator();

        var results = new List<TEntity>();
        while (feed.HasMoreResults)
        {
            var response = await feed.ReadNextAsync(ct);
            results.AddRange(response);
        }
        return results;
    }

    public async Task AddAsync(TEntity entity, CancellationToken ct = default)
    {
        await _container.CreateItemAsync(entity, cancellationToken: ct);
    }

    public async Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        await _container.UpsertItemAsync(entity, cancellationToken: ct);
    }

    public async Task<ItemResponse<TEntity>> UpsertItemAsync(TEntity entity, string partitionKey, CancellationToken ct = default)
    {
        return await _container.UpsertItemAsync(entity, new PartitionKey(partitionKey), cancellationToken: ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            await _container.DeleteItemAsync<TEntity>(id, new PartitionKey(id), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Attempted to delete non-existent item {Id}", id);
        }
    }

    public async Task<List<TEntity>> QueryParameterizedAsync(QueryDefinition queryDefinition, string? partitionKey = null, CancellationToken ct = default)
    {
        var requestOptions = partitionKey != null ? new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) } : null;
        using var iterator = _container.GetItemQueryIterator<TEntity>(queryDefinition, requestOptions: requestOptions);
        
        var results = new List<TEntity>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }
        return results;
    }
}
