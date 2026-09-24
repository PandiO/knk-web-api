using System;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// How long audit log entries are kept (docs/specs/user-management/DESIGN.md §7 item 3),
/// admin-only. Mirrors SalaryConfigurationController's singleton GET/PUT shape — raw endpoint,
/// no web-app UI/FormConfiguration, per that precedent for this kind of admin-only config.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuditLogRetentionConfigurationController : ControllerBase
{
    private readonly IAuditLogRetentionConfigurationService _service;

    public AuditLogRetentionConfigurationController(IAuditLogRetentionConfigurationService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AuditLogRetentionConfigurationDto), 200)]
    public async Task<ActionResult<AuditLogRetentionConfigurationDto>> Get()
    {
        var config = await _service.GetAsync();
        return Ok(config);
    }

    [HttpPut]
    [ProducesResponseType(typeof(AuditLogRetentionConfigurationDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<AuditLogRetentionConfigurationDto>> Update([FromBody] UpdateAuditLogRetentionConfigurationDto dto)
    {
        try
        {
            var updated = await _service.UpdateAsync(dto);
            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
