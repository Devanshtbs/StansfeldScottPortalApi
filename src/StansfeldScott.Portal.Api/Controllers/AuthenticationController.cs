using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StansfeldScott.Portal.Common.Models;
using StansfeldScott.Portal.Domain.Contracts;
using StansfeldScott.Portal.Infrastructure.ExternalClients;

namespace StansfeldScott.Portal.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(DataversePortalSignInService signIn) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("workspace-access/{contactId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<bool>>> CheckWorkspaceAccess(Guid contactId, CancellationToken cancellationToken)
    {
        var canAccess = await signIn.CanAccessWorkspaceAsync(contactId, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(canAccess,
            canAccess ? "Account is approved for workspace access." : "Account approval is required for workspace access."));
    }

    [AllowAnonymous]
    [HttpPost("sign-in")]
    [ProducesResponseType(typeof(ApiResponse<PortalSignInResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PortalSignInResult>>> SignIn(
        [FromBody] PortalSignInRequest request,
        CancellationToken cancellationToken)
    {
        var user = await signIn.AuthenticateAsync(
            request.Username,
            request.Password,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized(ApiResponse<object>.Fail("Invalid username or password."));
        }

        return Ok(ApiResponse<PortalSignInResult>.Ok(user, "Sign-in successful."));
    }
}
