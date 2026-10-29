using System.ComponentModel.DataAnnotations;

namespace StansfeldScott.Portal.Domain.Contracts;

public sealed class ValidateInvitationRequest
{
    [Required]
    public Guid? AccountId { get; init; }

    [Required, StringLength(256)]
    public string InvitationCode { get; init; } = "";
}

public sealed record InvitationValidationResult(bool Valid);
