using System.ComponentModel.DataAnnotations;

namespace StansfeldScott.Portal.Domain.Contracts;

public sealed class PortalSignInRequest
{
    [Required, StringLength(100)]
    public string Username { get; init; } = "";

    [Required, StringLength(256)]
    public string Password { get; init; } = "";
}

public sealed record PortalSignInResult(
    Guid ContactId,
    string FullName,
    Guid? AccountId,
    string? AccountName);
