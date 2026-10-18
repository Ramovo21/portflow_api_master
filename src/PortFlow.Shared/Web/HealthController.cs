using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;

namespace PortFlow.Shared.Web;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Dictionary<string, object> Health() => new()
    {
        ["status"] = "UP",
        ["time"] = InstantPattern.ExtendedIso.Format(SystemClock.Instance.GetCurrentInstant()),
    };
}
