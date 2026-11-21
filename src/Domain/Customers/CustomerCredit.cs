using System.Globalization;

namespace Retail360.Domain.Customers;

public sealed record OpenCharge(Guid EntryId, Guid SaleId, int ReceiptNumber, decimal OpenAmount);

public sealed record CreditAllocation(Guid ChargeEntryId, Guid SaleId, int ReceiptNumber, decimal Amount);

public static class CustomerCreditText
{
    public const decimal MaxAmount = 9999999999.99m;

    public const string Charge = "charge";

    public const string Payment = "payment";

    public static decimal RequireAmount(decimal value)
    {
        if (value <= 0 || value > MaxAmount || decimal.Round(value, 2) != value)
        {
            throw new CustomerRuleException(
                CustomerRuleFailure.Invalid,
                "Payment amount must be greater than zero with at most 2 decimal places.");
        }

        return value;
    }

    public static string PaymentFingerprint(Guid customerId, decimal amount)
    {
        return "customer|"
            + customerId.ToString("N")
            + "|amount|"
            + amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static string ChargeFingerprint(Guid saleId, Guid customerId, decimal amount)
    {
        return "charge|"
            + saleId.ToString("N")
            + "|"
            + customerId.ToString("N")
            + "|"
            + amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<CreditAllocation> Allocate(decimal payment, IReadOnlyList<OpenCharge> oldestFirst)
    {
        var amount = RequireAmount(payment);
        decimal outstanding = 0;
        foreach (var charge in oldestFirst)
        {
            if (charge.OpenAmount <= 0 || decimal.Round(charge.OpenAmount, 2) != charge.OpenAmount)
            {
                throw new CustomerRuleException(
                    CustomerRuleFailure.Conflict,
                    "Charge balance is not available.");
            }

            outstanding += charge.OpenAmount;
        }

        if (amount > outstanding)
        {
            throw new CustomerRuleException(
                CustomerRuleFailure.Conflict,
                "Payment is larger than the outstanding balance.");
        }

        var slices = new List<CreditAllocation>();
        var remaining = amount;
        foreach (var charge in oldestFirst)
        {
            if (remaining == 0)
            {
                break;
            }

            var slice = remaining < charge.OpenAmount ? remaining : charge.OpenAmount;
            slices.Add(new CreditAllocation(charge.EntryId, charge.SaleId, charge.ReceiptNumber, slice));
            remaining -= slice;
        }

        if (remaining != 0 || slices.Sum(slice => slice.Amount) != amount)
        {
            throw new CustomerRuleException(
                CustomerRuleFailure.Conflict,
                "Payment allocations must equal the payment amount.");
        }

        return slices;
    }
}
