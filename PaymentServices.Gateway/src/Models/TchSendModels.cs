namespace PaymentServices.Gateway.Models;

// ---------------------------------------------------------------------------
// Inbound request from RTPSend
// ---------------------------------------------------------------------------

/// <summary>
/// Inbound payload from RTPSend for POST /tptch/send.
/// Mirrors the existing Node tptch.send params contract.
/// </summary>
public sealed class TchSendRequest
{
    public string EvolveId { get; set; } = string.Empty;
    public string FintechId { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public bool UserIsBusiness { get; set; }
    public required AccountDetails SourceAccount { get; set; }
    public required AccountDetails DestinationAccount { get; set; }
}

/// <summary>
/// Account details for source or destination party.
/// </summary>
public sealed class AccountDetails
{
    public string AccountNumber { get; set; } = string.Empty;
    public string RoutingNumber { get; set; } = string.Empty;
    public required AccountName Name { get; set; }
    public AccountAddress? Address { get; set; }
    public string? PhoneNumber { get; set; }
}

/// <summary>
/// Name of the account holder — individual or business.
/// </summary>
public sealed class AccountName
{
    public string? First { get; set; }
    public string? Last { get; set; }
    public string? Company { get; set; }
}

/// <summary>
/// Address of the account holder.
/// </summary>
public sealed class AccountAddress
{
    public string[]? AddressLines { get; set; }
    public string? City { get; set; }
    public string? StateCode { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryISOCode { get; set; }
}

// ---------------------------------------------------------------------------
// Outbound responses
// ---------------------------------------------------------------------------

/// <summary>
/// Accepted response returned to RTPSend on successful intake.
/// HTTP 202 Accepted.
/// </summary>
public sealed class TchSendAcceptedResponse
{
    /// <summary>Echoed back from the request for correlation.</summary>
    public required string EvolveId { get; init; }

    /// <summary>Internal correlation ID for distributed tracing.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Human-readable status message.</summary>
    public string Status { get; init; } = "Accepted";

    /// <summary>UTC timestamp of acceptance.</summary>
    public DateTimeOffset AcceptedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Problem details response for validation or processing errors.
/// </summary>
public sealed class ProblemResponse
{
    public required string Title { get; init; }
    public required int Status { get; init; }
    public required string Detail { get; init; }
    public string? EvolveId { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }
}
