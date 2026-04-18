using FluentValidation;
using Microsoft.Extensions.Options;
using PaymentServices.Gateway.Models;

namespace PaymentServices.Gateway.Validators;

/// <summary>
/// Validates the inbound <see cref="TchSendRequest"/> from RTPSend.
/// Mirrors all validation logic from the Node validateTptchPayload method.
/// </summary>
public sealed class TchSendRequestValidator : AbstractValidator<TchSendRequest>
{
    private static readonly Regex AccountNumberPattern =
        new(@"^[0-9]{4,17}$", RegexOptions.Compiled);

    public TchSendRequestValidator(IOptions<GatewaySettings> settings)
    {
        var gatewaySettings = settings.Value;

        // -------------------------------------------------------------------------
        // EvolveId
        // -------------------------------------------------------------------------
        RuleFor(x => x.EvolveId)
            .NotEmpty()
            .WithMessage("No evolveId provided.")
            .WithErrorCode("INVALID_VALUE");

        // -------------------------------------------------------------------------
        // FintechId
        // -------------------------------------------------------------------------
        RuleFor(x => x.FintechId)
            .NotEmpty()
            .WithMessage("No fintechId provided.")
            .WithErrorCode("INVALID_VALUE");

        RuleFor(x => x.FintechId)
            .Must(id => gatewaySettings.GetAllowedFintechIds().Contains(id))
            .When(x => !string.IsNullOrWhiteSpace(x.FintechId))
            .WithMessage("Invalid fintechId value.")
            .WithErrorCode("INVALID_VALUE");

        // -------------------------------------------------------------------------
        // TaxId
        // -------------------------------------------------------------------------
        RuleFor(x => x.TaxId)
            .NotEmpty()
            .WithMessage("No taxId provided.")
            .WithErrorCode("INVALID_VALUE");

        // -------------------------------------------------------------------------
        // Amount
        // -------------------------------------------------------------------------
        RuleFor(x => x.Amount)
            .NotEmpty()
            .WithMessage("No amount provided.")
            .WithErrorCode("INVALID_VALUE");

        RuleFor(x => x.Amount)
            .Must(BeAValidAmount)
            .When(x => !string.IsNullOrWhiteSpace(x.Amount))
            .WithMessage("Invalid amount. Please check your formatting.")
            .WithErrorCode("INCORRECT_FORMAT");

        RuleFor(x => x.Amount)
            .Must(NotExceedTwoDecimalPlaces)
            .When(x => !string.IsNullOrWhiteSpace(x.Amount))
            .WithMessage("Amount must not exceed 2 decimal places.")
            .WithErrorCode("INCORRECT_FORMAT");

        // -------------------------------------------------------------------------
        // Source Account
        // -------------------------------------------------------------------------
        RuleFor(x => x.SourceAccount).NotNull()
            .WithMessage("sourceAccount is required.")
            .WithErrorCode("INVALID_VALUE");

        When(x => x.SourceAccount is not null, () =>
        {
            RuleFor(x => x.SourceAccount.AccountNumber)
                .NotEmpty()
                .WithMessage("No sourceAccount.accountNumber provided.")
                .WithErrorCode("INVALID_VALUE");

            RuleFor(x => x.SourceAccount.AccountNumber)
                .Must(n => AccountNumberPattern.IsMatch(n.Trim().Replace(" ", "")))
                .When(x => !string.IsNullOrWhiteSpace(x.SourceAccount.AccountNumber))
                .WithMessage("Source account number must contain 4-17 numeric digits.")
                .WithErrorCode("INVALID_FORMAT");

            RuleFor(x => x.SourceAccount.AccountNumber)
                .Must(n => gatewaySettings.GetSourceAccountPrefixes()
                    .Any(prefix => n.Trim().StartsWith(prefix)))
                .When(x => !string.IsNullOrWhiteSpace(x.SourceAccount?.AccountNumber)
                    && gatewaySettings.GetSourceAccountPrefixes().Any())
                .WithMessage("Invalid sourceAccount.accountNumber provided.")
                .WithErrorCode("INVALID_VALUE");

            RuleFor(x => x.SourceAccount.RoutingNumber)
                .NotEmpty()
                .Length(9)
                .WithMessage("Invalid sourceAccount.routingNumber provided.")
                .WithErrorCode("INVALID_VALUE");

            RuleFor(x => x.SourceAccount.Name)
                .NotNull()
                .WithMessage("No sourceAccount.name provided.")
                .WithErrorCode("INVALID_VALUE");

            RuleFor(x => x.SourceAccount.Name)
                .Must(HaveValidName)
                .When(x => x.SourceAccount.Name is not null)
                .WithMessage("sourceAccount.name must have either first+last or company.")
                .WithErrorCode("INVALID_VALUE");

            // Business account must have company name
            When(x => x.UserIsBusiness, () =>
            {
                RuleFor(x => x.SourceAccount.Name.Company)
                    .NotEmpty()
                    .WithMessage("Business account must have at minimum a company name.")
                    .WithErrorCode("INVALID_VALUE");
            });
        });

        // -------------------------------------------------------------------------
        // Destination Account
        // -------------------------------------------------------------------------
        RuleFor(x => x.DestinationAccount).NotNull()
            .WithMessage("destinationAccount is required.")
            .WithErrorCode("INVALID_VALUE");

        When(x => x.DestinationAccount is not null, () =>
        {
            RuleFor(x => x.DestinationAccount.AccountNumber)
                .NotEmpty()
                .WithMessage("No destinationAccount.accountNumber provided.")
                .WithErrorCode("INVALID_VALUE");

            RuleFor(x => x.DestinationAccount.AccountNumber)
                .Must(n => AccountNumberPattern.IsMatch(n.Trim().Replace(" ", "")))
                .When(x => !string.IsNullOrWhiteSpace(x.DestinationAccount.AccountNumber))
                .WithMessage("Destination account number must contain 4-17 numeric digits.")
                .WithErrorCode("INVALID_FORMAT");

            RuleFor(x => x.DestinationAccount.RoutingNumber)
                .NotEmpty()
                .Length(9)
                .WithMessage("Invalid destinationAccount.routingNumber provided.")
                .WithErrorCode("INVALID_VALUE");

            RuleFor(x => x.DestinationAccount.Name)
                .NotNull()
                .WithMessage("No destinationAccount.name provided.")
                .WithErrorCode("INVALID_VALUE");

            RuleFor(x => x.DestinationAccount.Name)
                .Must(HaveValidName)
                .When(x => x.DestinationAccount.Name is not null)
                .WithMessage("destinationAccount.name must have either first+last or company.")
                .WithErrorCode("INVALID_VALUE");
        });
    }

    private static bool BeAValidAmount(string amount)
    {
        return decimal.TryParse(amount, out var value) && value > 0;
    }

    private static bool NotExceedTwoDecimalPlaces(string amount)
    {
        if (!amount.Contains('.')) return true;
        var decimalPart = amount.Split('.')[1];
        return decimalPart.Length <= 2;
    }

    private static bool HaveValidName(AccountName name)
    {
        var hasCompany = !string.IsNullOrWhiteSpace(name.Company);
        var hasIndividual = !string.IsNullOrWhiteSpace(name.First)
                            && !string.IsNullOrWhiteSpace(name.Last);
        return hasCompany || hasIndividual;
    }
}
