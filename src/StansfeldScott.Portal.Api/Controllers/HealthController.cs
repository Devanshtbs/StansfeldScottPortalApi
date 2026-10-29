using Microsoft.AspNetCore.Mvc;
using StansfeldScott.Portal.Common.Models;

namespace StansfeldScott.Portal.Api.Controllers;


[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<object>> Get() =>
        Ok(ApiResponse<object>.Ok(new { status = "healthy", utc = DateTime.UtcNow }));
}
