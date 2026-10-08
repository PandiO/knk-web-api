using knkwebapi_v2.Attributes;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Controllers;

[ApiController]
[Route("api/[controller]")]
// Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.world or the game server; reads stay logged-in.
[RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
public class LocationsController : ControllerBase
{
    private readonly ILocationService _service;

    public LocationsController(ILocationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _service.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("{id:int}", Name = nameof(GetLocationById))]
    public async Task<IActionResult> GetLocationById(int id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LocationDto locationDto)
    {
        if (locationDto == null) return BadRequest();
        try
        {
            var created = await _service.CreateAsync(locationDto);
            return CreatedAtRoute(nameof(GetLocationById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LocationDto locationDto)
    {
        if (locationDto == null) return BadRequest();
        try
        {
            await _service.UpdateAsync(id, locationDto);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (DbUpdateException ex)
        {
            // Still referenced (e.g. by a siege scenario, whose FKs restrict deletes): 409, not 500.
            return Conflict(new { code = "DbConstraint", message = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPost("search")]
    public async Task<ActionResult<PagedResultDto<LocationDto>>> SearchLocations([FromBody] PagedQueryDto query)
    {
        var result = await _service.SearchAsync(query);
        return Ok(result);
    }
}
