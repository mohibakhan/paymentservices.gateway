using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PaymentServices.Gateway.Models;
using PaymentServices.Gateway.Services;

namespace PaymentServices.Gateway.Functions;

/// <summary>
/// HTTP Trigger — receives POST /tptch/send from RTPSend.
/// Validates the payload, checks idempotency, and publishes to Service Bus.
/// Returns 202 Accepted immediately — processing is fully async.
/// </summary>
public sealed class TchSendFunction
{
    private readonly IGatewayService _gatewayService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IValidator<TchSendRequest> _validator;
    private readonly ILogger<TchSendFunction> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public TchSendFunction(
        IGatewayService gatewayService,
        IIdempotencyService idempotencyService,
        IValidator<TchSendRequest> validator,
        ILogger<TchSendFunction> logger)
    {
        _gatewayService = gatewayService;
        _idempotencyService = idempotencyService;
        _validator = validator;
        _logger = logger;
    }

    [Function(nameof(TchSendFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "tptch/send")]
        HttpRequestData req,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "TchSend request received. CorrelationId={CorrelationId}", correlationId);

        // -------------------------------------------------------------------------
        // Deserialize
        // -------------------------------------------------------------------------
        TchSendRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<TchSendRequest>(
                req.Body, _jsonOptions, cancellationToken);

            if (request is null)
                return await BadRequestAsync(req, "Request body is required.", null, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Deserialization failed. CorrelationId={CorrelationId} Error={Error}",
                correlationId, ex.Message);
            return await BadRequestAsync(req, "Invalid JSON payload.", null, cancellationToken);
        }

        _logger.LogInformation(
            "TchSend deserialised. EvolveId={EvolveId} CorrelationId={CorrelationId}",
            request.EvolveId, correlationId);

        // -------------------------------------------------------------------------
        // Validate
        // -------------------------------------------------------------------------
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            _logger.LogWarning(
                "Validation failed. EvolveId={EvolveId} CorrelationId={CorrelationId} Errors={@Errors}",
                request.EvolveId, correlationId, errors);

            return await ProblemAsync(req, new ProblemResponse
            {
                Title = "Validation Failed",
                Status = (int)HttpStatusCode.BadRequest,
                Detail = "One or more validation errors occurred.",
                EvolveId = request.EvolveId,
                Errors = errors
            }, HttpStatusCode.BadRequest, cancellationToken);
        }

        // -------------------------------------------------------------------------
        // Idempotency check
        // -------------------------------------------------------------------------
        var isDuplicate = await _idempotencyService.IsDuplicateAsync(
            request.EvolveId, correlationId, cancellationToken);

        if (isDuplicate)
        {
            _logger.LogWarning(
                "Duplicate request rejected. EvolveId={EvolveId} CorrelationId={CorrelationId}",
                request.EvolveId, correlationId);

            return await ProblemAsync(req, new ProblemResponse
            {
                Title = "Duplicate Request",
                Status = (int)HttpStatusCode.Conflict,
                Detail = $"A request with evolveId '{request.EvolveId}' has already been received.",
                EvolveId = request.EvolveId
            }, HttpStatusCode.Conflict, cancellationToken);
        }

        // -------------------------------------------------------------------------
        // Accept and publish
        // -------------------------------------------------------------------------
        try
        {
            await _gatewayService.AcceptAsync(request, correlationId, cancellationToken);

            var accepted = req.CreateResponse(HttpStatusCode.Accepted);
            accepted.Headers.Add("Content-Type", "application/json");
            accepted.Headers.Add("X-Correlation-Id", correlationId);

            await accepted.WriteStringAsync(
                JsonSerializer.Serialize(new TchSendAcceptedResponse
                {
                    EvolveId = request.EvolveId,
                    CorrelationId = correlationId
                }, _jsonOptions), cancellationToken);

            _logger.LogInformation(
                "TchSend accepted. EvolveId={EvolveId} CorrelationId={CorrelationId}",
                request.EvolveId, correlationId);

            return accepted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to accept TchSend request. EvolveId={EvolveId} CorrelationId={CorrelationId}",
                request.EvolveId, correlationId);

            return await ProblemAsync(req, new ProblemResponse
            {
                Title = "Internal Server Error",
                Status = (int)HttpStatusCode.InternalServerError,
                Detail = "An unexpected error occurred. Please try again.",
                EvolveId = request.EvolveId
            }, HttpStatusCode.InternalServerError, cancellationToken);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<HttpResponseData> BadRequestAsync(
        HttpRequestData req,
        string detail,
        string? evolveId,
        CancellationToken cancellationToken)
    {
        return await ProblemAsync(req, new ProblemResponse
        {
            Title = "Bad Request",
            Status = (int)HttpStatusCode.BadRequest,
            Detail = detail,
            EvolveId = evolveId
        }, HttpStatusCode.BadRequest, cancellationToken);
    }

    private static async Task<HttpResponseData> ProblemAsync(
        HttpRequestData req,
        ProblemResponse problem,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/problem+json");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(problem, _jsonOptions), cancellationToken);
        return response;
    }
}
