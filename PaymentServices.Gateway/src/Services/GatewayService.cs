using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.Gateway.Models;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Infrastructure;
using PaymentServices.Shared.Interfaces;
using PaymentServices.Shared.Messages;
using PaymentServices.Shared.Models;

namespace PaymentServices.Gateway.Services;

public interface IGatewayService
{
    Task<string> AcceptAsync(TchSendRequest request, string correlationId, CancellationToken cancellationToken = default);
}

public sealed class GatewayService : IGatewayService
{
    private readonly IIdempotencyService _idempotencyService;
    private readonly IServiceBusPublisher _serviceBusPublisher;
    private readonly ICosmosRepository<CosmosTransaction> _transactionRepository;
    private readonly ILogger<GatewayService> _logger;

    public GatewayService(
        IIdempotencyService idempotencyService,
        IServiceBusPublisher serviceBusPublisher,
        [FromKeyedServices("transactions")] Container container,
        ILogger<GatewayService> logger)
    {
        _idempotencyService = idempotencyService;
        _serviceBusPublisher = serviceBusPublisher;
        _transactionRepository = new CosmosRepository<CosmosTransaction>(container);
        _logger = logger;
    }

    public async Task<string> AcceptAsync(
        TchSendRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Normalize account numbers — trim and remove whitespace (mirrors Node behavior)
        request.SourceAccount.AccountNumber =
            request.SourceAccount.AccountNumber.Trim().Replace(" ", "");
        request.DestinationAccount.AccountNumber =
            request.DestinationAccount.AccountNumber.Trim().Replace(" ", "");

        // Normalize taxId — remove hyphens
        request.TaxId = request.TaxId.Replace("-", "");

        // Build the pipeline message
        var message = new PaymentMessage
        {
            EvolveId = request.EvolveId,
            FintechId = request.FintechId,
            CorrelationId = correlationId,
            Amount = request.Amount,
            TaxId = request.TaxId,
            UserIsBusiness = request.UserIsBusiness,
            State = TransactionState.Received,
            Source = new PaymentParty
            {
                AccountNumber = request.SourceAccount.AccountNumber,
                RoutingNumber = request.SourceAccount.RoutingNumber,
                Name = new PaymentServices.Shared.Messages.PartyName
                {
                    First = request.SourceAccount.Name.First,
                    Last = request.SourceAccount.Name.Last,
                    Company = request.SourceAccount.Name.Company
                },
                Address = request.SourceAccount.Address is null ? null
                    : new PaymentServices.Shared.Messages.PartyAddress
                    {
                        Line1 = request.SourceAccount.Address.AddressLines?.FirstOrDefault(),
                        City = request.SourceAccount.Address.City,
                        State = request.SourceAccount.Address.StateCode,
                        PostalCode = request.SourceAccount.Address.PostalCode,
                        Country = request.SourceAccount.Address.CountryISOCode
                    },
                PhoneNumber = request.SourceAccount.PhoneNumber
            },
            Destination = new PaymentParty
            {
                AccountNumber = request.DestinationAccount.AccountNumber,
                RoutingNumber = request.DestinationAccount.RoutingNumber,
                Name = new PaymentServices.Shared.Messages.PartyName
                {
                    First = request.DestinationAccount.Name.First,
                    Last = request.DestinationAccount.Name.Last,
                    Company = request.DestinationAccount.Name.Company
                },
                Address = request.DestinationAccount.Address is null ? null
                    : new PaymentServices.Shared.Messages.PartyAddress
                    {
                        Line1 = request.DestinationAccount.Address.AddressLines?.FirstOrDefault(),
                        City = request.DestinationAccount.Address.City,
                        State = request.DestinationAccount.Address.StateCode,
                        PostalCode = request.DestinationAccount.Address.PostalCode,
                        Country = request.DestinationAccount.Address.CountryISOCode
                    },
                PhoneNumber = request.DestinationAccount.PhoneNumber
            }
        };

        // Persist initial transaction state to Cosmos
        var cosmosTransaction = new CosmosTransaction
        {
            Id = request.EvolveId,
            EvolveId = request.EvolveId,
            CorrelationId = correlationId,
            FintechId = request.FintechId,
            Amount = request.Amount,
            TaxId = request.TaxId,
            UserIsBusiness = request.UserIsBusiness,
            State = TransactionState.Received,
            ReceivedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };

        await _transactionRepository.CreateAsync(
            cosmosTransaction, request.EvolveId, cancellationToken);

        _logger.LogInformation(
            "Transaction record created. EvolveId={EvolveId} CorrelationId={CorrelationId}",
            request.EvolveId, correlationId);

        // Advance state and publish to Service Bus
        message.State = TransactionState.AccountResolutionPending;
        await _serviceBusPublisher.PublishAsync(message, cancellationToken);

        _logger.LogInformation(
            "Message published to Service Bus. EvolveId={EvolveId} State={State}",
            request.EvolveId, message.State);

        return correlationId;
    }
}
