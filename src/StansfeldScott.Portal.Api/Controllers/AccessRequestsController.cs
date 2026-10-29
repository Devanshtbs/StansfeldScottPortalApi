using Microsoft.AspNetCore.Mvc;
using StansfeldScott.Portal.Common.Models;
using StansfeldScott.Portal.Infrastructure.ExternalClients;
using StansfeldScott.Portal.Domain.Contracts;

namespace StansfeldScott.Portal.Api.Controllers;

[ApiController]
[Route("api/access-requests")]
public sealed class AccessRequestsController(DataverseOnboardingService onboarding) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AccessRequestResult>), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Submit([FromBody] AccessRequest request, CancellationToken cancellationToken)
    {
        await onboarding.SubmitAsync(request, cancellationToken);
        return Accepted(ApiResponse<AccessRequestResult>.Ok(
            new AccessRequestResult("PendingReview"),
            "Your request was sent for CRM review."));
    }
}
