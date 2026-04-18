using PaymentServices.Shared.Models;

namespace PaymentServices.Gateway.Models;

/// <summary>
/// Gateway-specific settings bound from <c>app:AppSettings</c>.
/// Extends the shared <see cref="AppSettings"/> base.
/// </summary>
public sealed class GatewaySettings : AppSettings
{
    /// <summary>
    /// Allowed FintechId values. Comma-separated in config.
    /// e.g. "fintech-id-1,fintech-id-2,fintech-id-3"
    /// </summary>
    public string ALLOWED_FINTECH_IDS { get; set; } = string.Empty;

    /// <summary>
    /// Allowed source account number prefixes. Comma-separated.
    /// Replaces TCH_SOURCE_ACCOUNT_BASE from the Node service.
    /// </summary>
    public string SOURCE_ACCOUNT_NUMBER_PREFIXES { get; set; } = string.Empty;

    /// <summary>Cosmos DB container for idempotency checks.</summary>
    public string COSMOS_IDEMPOTENCY_CONTAINER { get; set; } = "tch-send-idempotency";

    /// <summary>Cosmos DB container for transaction state.</summary>
    public string COSMOS_TRANSACTIONS_CONTAINER { get; set; } = "tch-send-transactions";

    /// <summary>
    /// Returns the allowed FintechId values as an array.
    /// </summary>
    public IReadOnlyList<string> GetAllowedFintechIds() =>
        ALLOWED_FINTECH_IDS
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Returns the allowed source account number prefixes as an array.
    /// </summary>
    public IReadOnlyList<string> GetSourceAccountPrefixes() =>
        SOURCE_ACCOUNT_NUMBER_PREFIXES
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
