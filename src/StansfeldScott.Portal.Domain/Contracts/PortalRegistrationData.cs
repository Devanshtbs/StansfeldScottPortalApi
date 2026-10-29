namespace StansfeldScott.Portal.Domain.Contracts;

public sealed record InvitationRegistrationInfo(Guid AccountId, string CompanyName, string AccountNumber);
public sealed record CountryOption(Guid CountryId, string Name);
