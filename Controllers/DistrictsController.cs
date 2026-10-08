using Microsoft.Extensions.Logging;
using knkwebapi_v2.Attributes;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;
using knkwebapi_v2.Dtos;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.world or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    public class DistrictsController : ControllerBase
    {
        private readonly IDistrictService _service;

        private readonly ILogger<DistrictsController> _logger;

        public DistrictsController(IDistrictService service, ILogger<DistrictsController>? logger = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DistrictsController>.Instance;
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:int}", Name = nameof(GetDistrictById))]
        public async Task<IActionResult> GetDistrictById(int id, [FromQuery] string? townFields = null, [FromQuery] string? streetFields = null, [FromQuery] string? structureFields = null)
        {
            var item = await _service.GetByIdAsync(id, townFields, streetFields, structureFields);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DistrictDto districtDto)
        {
            if (districtDto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(districtDto);
                return CreatedAtRoute(nameof(GetDistrictById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DistrictDto districtDto)
        {
            if (districtDto == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, districtDto);
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
                _logger.LogError(ex, "Deleting district {Id} hit a database constraint", id);
                return Conflict(new { code = "DbConstraint", message = "This district is still in use and can't be deleted." });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<DistrictListDto>>> SearchDistricts([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }
    }
}
