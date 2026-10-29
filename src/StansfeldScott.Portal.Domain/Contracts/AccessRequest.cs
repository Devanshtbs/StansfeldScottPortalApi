using System.ComponentModel.DataAnnotations;

namespace StansfeldScott.Portal.Domain.Contracts;

public sealed class AccessRequest
{
    [Required] public Guid? AccountId { get; init; }
    [Required, StringLength(128)] public string InvitationCode { get; init; } = "";
    [Required, StringLength(80)] public string FirstName { get; init; } = "";
    [Required, StringLength(80)] public string LastName { get; init; } = "";
    [Required, StringLength(100, MinimumLength = 3)] public string Username { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; init; } = "";
    [StringLength(100)] public string? JobTitle { get; init; }
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(50)] public string Phone { get; init; } = "";
    [Required, StringLength(80)] public string Region { get; init; } = "";
    [Required] public Guid? CountryId { get; init; }
}

public sealed record AccessRequestResult(string Status);
