using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GateStructuresController : ControllerBase
    {
        private readonly IGateStructureService _service;

        public GateStructuresController(IGateStructureService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int? pageNumber = null,
            [FromQuery] int? pageSize = null,
            [FromQuery] string? searchTerm = null,
            [FromQuery] string? sortBy = null,
            [FromQuery] bool? sortDescending = null,
            [FromQuery] int? streetId = null,
            [FromQuery] int? districtId = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] string? gateType = null,
            [FromQuery] bool? isOpened = null)
        {
            var hasQueryFilters = pageNumber.HasValue || pageSize.HasValue || !string.IsNullOrWhiteSpace(searchTerm) ||
                                  !string.IsNullOrWhiteSpace(sortBy) || sortDescending.HasValue || streetId.HasValue ||
                                  districtId.HasValue || isActive.HasValue || !string.IsNullOrWhiteSpace(gateType) ||
                                  isOpened.HasValue;

            if (!hasQueryFilters)
            {
                var items = await _service.GetAllAsync();
                return Ok(items);
            }

            var query = new PagedQueryDto
            {
                PageNumber = pageNumber ?? 1,
                PageSize = pageSize ?? 10,
                SearchTerm = searchTerm,
                SortBy = sortBy,
                SortDescending = sortDescending ?? false,
                Filters = new Dictionary<string, string>()
            };

            // isActive/gateType/isOpened filter on whether at least one of the structure's
            // doors matches (item 5's multi-door support moved these fields to GateDoor).
            if (streetId.HasValue) query.Filters["streetId"] = streetId.Value.ToString();
            if (districtId.HasValue) query.Filters["districtId"] = districtId.Value.ToString();
            if (isActive.HasValue) query.Filters["isActive"] = isActive.Value.ToString();
            if (!string.IsNullOrWhiteSpace(gateType)) query.Filters["gateType"] = gateType;
            if (isOpened.HasValue) query.Filters["isOpened"] = isOpened.Value.ToString();

            if (!query.Filters.Any()) query.Filters = null;

            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        [HttpGet("{id:int}", Name = "GetGateStructureById")]
        public async Task<IActionResult> GetById(int id, [FromQuery] bool includeSnapshots = false)
        {
            var item = includeSnapshots
                ? await _service.GetByIdWithSnapshotsAsync(id)
                : await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpGet("domain/{domainId:int}")]
        public async Task<IActionResult> GetByDomain(int domainId)
        {
            if (domainId <= 0) return BadRequest("Invalid domainId.");
            try
            {
                var items = await _service.GetGatesByDomainAsync(domainId);
                return Ok(items);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GateStructureDto gateStructureDto)
        {
            if (gateStructureDto == null) return BadRequest(ModelState);

            var validationError = ValidateGatePayload(gateStructureDto);
            if (!string.IsNullOrWhiteSpace(validationError))
                return BadRequest(validationError);

            try
            {
                var created = await _service.CreateAsync(gateStructureDto);
                return CreatedAtRoute("GetGateStructureById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] GateStructureDto gateStructureDto)
        {
            if (gateStructureDto == null) return BadRequest(ModelState);

            var validationError = ValidateGatePayload(gateStructureDto);
            if (!string.IsNullOrWhiteSpace(validationError))
                return BadRequest(validationError);

            try
            {
                await _service.UpdateAsync(id, gateStructureDto);
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
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<GateStructureListDto>>> SearchGateStructures([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        // Sets/clears the structure-level cascading overrides (decision 5.0-B). Permission
        // model for who may call this is still open - see
        // GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md item 5.3 "still open" #5 - intended for
        // admin commands/permissions and the future Siege capture event.
        [HttpPatch("{id:int}/overrides")]
        public async Task<IActionResult> UpdateOverrides(int id, [FromBody] GateStructureOverridesUpdateDto request)
        {
            if (id <= 0) return BadRequest("Invalid id.");
            if (request == null) return BadRequest();

            try
            {
                await _service.UpdateOverridesAsync(id, request);
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

        private static string? ValidateGatePayload(GateStructureDto gateStructureDto)
        {
            if (gateStructureDto.OpenedStateOverride.HasValue &&
                !Enum.IsDefined(typeof(GateDoorOpenState), gateStructureDto.OpenedStateOverride.Value))
            {
                return "Invalid OpenedStateOverride.";
            }

            if (gateStructureDto.HealthDisplayModeOverride.HasValue &&
                !Enum.IsDefined(typeof(HealthDisplayMode), gateStructureDto.HealthDisplayModeOverride.Value))
            {
                return "Invalid HealthDisplayModeOverride.";
            }

            if (gateStructureDto.GateNameDisplayModeOverride.HasValue &&
                !Enum.IsDefined(typeof(GateInfoDisplayMode), gateStructureDto.GateNameDisplayModeOverride.Value))
            {
                return "Invalid GateNameDisplayModeOverride.";
            }

            if (gateStructureDto.StatusDisplayModeOverride.HasValue &&
                !Enum.IsDefined(typeof(GateInfoDisplayMode), gateStructureDto.StatusDisplayModeOverride.Value))
            {
                return "Invalid StatusDisplayModeOverride.";
            }

            return null;
        }
    }
}
