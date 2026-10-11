using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace knkwebapi_v2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHealth()
        {
            var version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version?.ToString() ?? "1.0.0";

            var response = new
            {
                status = "ok",
                version = version,
                timestampUtc = DateTime.UtcNow.ToString("o")
            };

            return Ok(response);
        }
    }
}
