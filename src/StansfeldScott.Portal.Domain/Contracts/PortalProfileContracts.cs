using System.ComponentModel.DataAnnotations;

namespace StansfeldScott.Portal.Domain.Contracts;

public sealed record PortalProfileResult(
    PortalContactProfile User,
    PortalAccountProfile? Account,
    IReadOnlyList<PortalContactProfile> OtherContacts);

public sealed record PortalContactProfile(
    Guid ContactId,
    string FirstName,
    string LastName,
    string FullName,
    string? Email,
    string? Phone,
    string? JobTitle);

public sealed record PortalAccountProfile(
    Guid AccountId,
    string Name,
    string? AccountNumber,
    string? Phone,
    string? Website,
    string? City,
    string? StateOrProvince,
    string? Country,
    string? PostalCode);

public sealed class UpdatePortalContactRequest
{
    [Required, StringLength(80)] public string FirstName { get; init; } = "";
    [Required, StringLength(80)] public string LastName { get; init; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [StringLength(50)] public string? Phone { get; init; }
    [StringLength(100)] public string? JobTitle { get; init; }
}

public sealed class RelatedContactRequest
{
    [Required, StringLength(80)] public string FirstName { get; init; } = "";
    [Required, StringLength(80)] public string LastName { get; init; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [StringLength(50)] public string? Phone { get; init; }
    [StringLength(100)] public string? JobTitle { get; init; }
}

public sealed class UpdatePortalAccountRequest
{
    [Required, StringLength(160)] public string Name { get; init; } = "";
    [StringLength(40)] public string? AccountNumber { get; init; }
    [StringLength(50)] public string? Phone { get; init; }
    [Url, StringLength(200)] public string? Website { get; init; }
    [StringLength(80)] public string? City { get; init; }
    [StringLength(80)] public string? StateOrProvince { get; init; }
    [StringLength(100)] public string? Country { get; init; }
    [StringLength(20)] public string? PostalCode { get; init; }
}
