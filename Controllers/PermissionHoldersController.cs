using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers;

/// <summary>Read-only lookup used by FormWizard relationship fields targeting PermissionHolder.</summary>
[ApiController]
[Route("api/[controller]")]
public class PermissionHoldersController : ControllerBase
{
    private readonly IPermissionHolderService _service;

    public PermissionHoldersController(IPermissionHolderService service)
    {
        _service = service;
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PermissionHolderListDto>> GetById(int id)
    {
        var holder = await _service.GetByIdAsync(id);
        return holder == null ? NotFound() : Ok(holder);
    }

    [HttpPost("search")]
    public async Task<ActionResult<PagedResultDto<PermissionHolderListDto>>> Search([FromBody] PagedQueryDto query)
    {
        return Ok(await _service.SearchAsync(query));
    }
}
