using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using PaymentServices.Shared.Infrastructure;
using PaymentServices.Shared.Interfaces;
using PaymentServices.Shared.Models;

namespace PaymentServices.Gateway.Services;

public interface IIdempotencyService
{
    /// <summary>
    /// Returns true if this evolveId has already been processed (duplicate).
    /// Returns false and records the evolveId if it is new.
    /// </summary>
    Task<bool> IsDuplicateAsync(string evolveId, string correlationId, CancellationToken cancellationToken = default);
}

public sealed class IdempotencyService : IIdempotencyService
{
    private readonly ICosmosRepository<CosmosIdempotency> _repository;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(
        [FromKeyedServices("idempotency")] Container container,
        ILogger<IdempotencyService> logger)
    {
        _repository = new CosmosRepository<CosmosIdempotency>(container);
        _logger = logger;
    }

    public async Task<bool> IsDuplicateAsync(
        string evolveId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Check if this evolveId already exists
        var existing = await _repository.GetAsync(evolveId, evolveId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogWarning(
                "Duplicate request detected for EvolveId={EvolveId} CorrelationId={CorrelationId}",
                evolveId, correlationId);
            return true;
        }

        // Record this evolveId to prevent future duplicates
        var idempotencyRecord = new CosmosIdempotency
        {
            Id = evolveId,
            EvolveId = evolveId,
            CorrelationId = correlationId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Ttl = 86400 // 24 hours
        };

        await _repository.CreateAsync(idempotencyRecord, evolveId, cancellationToken);

        _logger.LogInformation(
            "Idempotency record created for EvolveId={EvolveId}", evolveId);

        return false;
    }
}
