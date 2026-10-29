using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StansfeldScott.Portal.Common.Models;
using StansfeldScott.Portal.Domain.Contracts;
using StansfeldScott.Portal.Infrastructure.ExternalClients;

namespace StansfeldScott.Portal.Api.Controllers;

[ApiController]
[Route("api/invitations")]
public sealed class InvitationsController(
    DataverseInvitationValidationService invitations,
    DataverseRegistrationDataService registrationData) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ApiResponse<InvitationValidationResult>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<InvitationValidationResult>>> Validate(
        [FromBody] ValidateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var valid = await invitations.ValidateAsync(
            request.AccountId!.Value,
            request.InvitationCode,
            cancellationToken);

        var response = ApiResponse<InvitationValidationResult>.Ok(
            new InvitationValidationResult(valid),
            valid ? "Invitation is valid." : "Invitation is invalid or expired.");

        return Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("registration-info")]
    [ProducesResponseType(typeof(ApiResponse<InvitationRegistrationInfo>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<InvitationRegistrationInfo>>> RegistrationInfo(
        [FromBody] ValidateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var info = await registrationData.GetInvitationInfoAsync(
            request.AccountId!.Value, request.InvitationCode, cancellationToken);
        return info is null
            ? BadRequest(ApiResponse<object>.Fail("Invitation is invalid or expired."))
            : Ok(ApiResponse<InvitationRegistrationInfo>.Ok(info, "Invitation and Account are valid."));
    }

    [AllowAnonymous]
    [HttpGet("countries")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CountryOption>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CountryOption>>>> Countries(CancellationToken cancellationToken)
    {
        var countries = await registrationData.GetCountriesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CountryOption>>.Ok(countries, "Countries loaded."));
    }
}
