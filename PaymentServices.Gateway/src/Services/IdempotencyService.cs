using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using PaymentServices.Shared.Models;

namespace PaymentServices.Gateway.Services;

public interface IIdempotencyService
{
    /// <summary>
    /// Returns true if this evolveId has already been processed (duplicate).
    /// Returns false and records the evolveId if it is new.
    ///
    /// Uses a single conditional CreateItemAsync instead of read-then-write.
    /// A 409 Conflict response from Cosmos means the record already exists.
    /// This halves the number of Cosmos round trips per payment at the Gateway.
    /// </summary>
    Task<bool> IsDuplicateAsync(
        string evolveId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class IdempotencyService : IIdempotencyService
{
    private readonly Container _container;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(
        [FromKeyedServices("idempotency")] Container container,
        ILogger<IdempotencyService> logger)
    {
        _container = container;
        _logger = logger;
    }

    public async Task<bool> IsDuplicateAsync(
        string evolveId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var idempotencyRecord = new CosmosIdempotency
        {
            Id = evolveId,
            EvolveId = evolveId,
            CorrelationId = correlationId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Ttl = 86400 // 24 hours
        };

        try
        {
            // Single Cosmos operation — attempt to create.
            // If the record already exists Cosmos returns 409 Conflict.
            await _container.CreateItemAsync(
                idempotencyRecord,
                new PartitionKey(evolveId),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Idempotency record created. EvolveId={EvolveId}", evolveId);

            return false; // New request — not a duplicate
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // 409 = record already exists = duplicate request
            _logger.LogWarning(
                "Duplicate request detected. EvolveId={EvolveId} CorrelationId={CorrelationId}",
                evolveId, correlationId);

            return true;
        }
    }
}
