namespace BrunoVehicleHire.Application.Customers.Commands;

/// <summary>
/// The one definition of what a customer phone number may look like, shared by
/// <see cref="CreateCustomerCommandValidator"/> and <see cref="UpdateCustomerCommandValidator"/>
/// so the two can never drift apart. A 400-level shape check, like the email-format rule -- the
/// domain (<c>Customer.Create</c>/<c>Update</c>) still only requires a non-blank value.
/// </summary>
public static class PhoneNumberRules
{
    /// <summary>Digits only (no letters, spaces, dashes or a leading +), at most 10 of them.</summary>
    public const string DigitsOnlyUpToTenPattern = @"^[0-9]{1,10}$";

    public const string Message = "PhoneNumber must contain digits only, with a maximum of 10 digits.";
}
