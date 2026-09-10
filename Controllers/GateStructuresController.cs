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

        private static readonly HashSet<string> ValidFaceDirections = new(StringComparer.OrdinalIgnoreCase)
        {
            "north",
            "north-east",
            "east",
            "south-east",
            "south",
            "south-west",
            "west",
            "north-west"
        };

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

        [HttpPut("{id:int}/state")]
        public async Task<IActionResult> UpdateState(int id, [FromBody] GateStateUpdateDto request)
        {
            if (id <= 0) return BadRequest("Invalid id.");
            if (request == null) return BadRequest();

            try
            {
                await _service.UpdateStateAsync(id, request.IsOpened, request.IsDestroyed, request.IsJammed);
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

        [HttpPut("{id:int}/health")]
        public async Task<IActionResult> UpdateHealth(int id, [FromBody] GateHealthUpdateDto request)
        {
            if (id <= 0) return BadRequest("Invalid id.");
            if (request == null) return BadRequest();

            try
            {
                await _service.UpdateHealthAsync(id, request.HealthCurrent);
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

        [HttpPut("{id:int}/operational-settings")]
        public async Task<IActionResult> UpdateOperationalSettings(int id, [FromBody] GateOperationalSettingsUpdateDto request)
        {
            if (id <= 0) return BadRequest("Invalid id.");
            if (request == null) return BadRequest();

            try
            {
                await _service.UpdateOperationalSettingsAsync(id, request.IsActive, request.IsInvincible);
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

        [HttpGet("{id:int}/snapshots")]
        public async Task<IActionResult> GetSnapshots(int id)
        {
            if (id <= 0) return BadRequest("Invalid gateId.");
            try
            {
                var snapshots = await _service.GetBlockSnapshotsAsync(id);
                return Ok(snapshots);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{id:int}/snapshots/bulk")]
        public async Task<IActionResult> AddSnapshots(int id, [FromBody] IEnumerable<GateBlockSnapshotCreateDto> snapshots)
        {
            if (id <= 0) return BadRequest("Invalid gateId.");
            if (snapshots == null || !snapshots.Any()) return BadRequest("Snapshots collection cannot be null or empty.");

            try
            {
                await _service.AddBlockSnapshotsAsync(id, snapshots);
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

        [HttpDelete("{id:int}/snapshots")]
        public async Task<IActionResult> ClearSnapshots(int id)
        {
            if (id <= 0) return BadRequest("Invalid gateId.");
            try
            {
                await _service.ClearBlockSnapshotsAsync(id);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // Opened-block snapshot endpoints - mirror the snapshot endpoints above exactly, for
        // the separately-scanned fully-open shape. See ROTATION_GAP_FILL_DESIGN.md.
        [HttpGet("{id:int}/openedSnapshots")]
        public async Task<IActionResult> GetOpenedSnapshots(int id)
        {
            if (id <= 0) return BadRequest("Invalid gateId.");
            try
            {
                var snapshots = await _service.GetOpenedBlockSnapshotsAsync(id);
                return Ok(snapshots);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{id:int}/openedSnapshots/bulk")]
        public async Task<IActionResult> AddOpenedSnapshots(int id, [FromBody] IEnumerable<GateOpenedBlockSnapshotCreateDto> snapshots)
        {
            if (id <= 0) return BadRequest("Invalid gateId.");
            if (snapshots == null || !snapshots.Any()) return BadRequest("Snapshots collection cannot be null or empty.");

            try
            {
                await _service.AddOpenedBlockSnapshotsAsync(id, snapshots);
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

        [HttpDelete("{id:int}/openedSnapshots")]
        public async Task<IActionResult> ClearOpenedSnapshots(int id)
        {
            if (id <= 0) return BadRequest("Invalid gateId.");
            try
            {
                await _service.ClearOpenedBlockSnapshotsAsync(id);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private static string? ValidateGatePayload(GateStructureDto gateStructureDto)
        {
            if (string.IsNullOrWhiteSpace(gateStructureDto.FaceDirection) ||
                !ValidFaceDirections.Contains(gateStructureDto.FaceDirection))
            {
                return $"Invalid FaceDirection. Must be one of: {string.Join(", ", ValidFaceDirections)}";
            }

            if (!Enum.IsDefined(typeof(GateType), gateStructureDto.GateType))
            {
                return "Invalid GateType.";
            }

            if (!Enum.IsDefined(typeof(MotionType), gateStructureDto.MotionType))
            {
                return "Invalid MotionType.";
            }

            if (!Enum.IsDefined(typeof(GeometryDefinitionMode), gateStructureDto.GeometryDefinitionMode))
            {
                return "Invalid GeometryDefinitionMode.";
            }

            if (!Enum.IsDefined(typeof(TileEntityPolicy), gateStructureDto.TileEntityPolicy))
            {
                return "Invalid TileEntityPolicy.";
            }

            if (!Enum.IsDefined(typeof(HealthDisplayMode), gateStructureDto.HealthDisplayMode))
            {
                return "Invalid HealthDisplayMode.";
            }

            if (!Enum.IsDefined(typeof(GateInfoDisplayMode), gateStructureDto.GateNameDisplayMode))
            {
                return "Invalid GateNameDisplayMode.";
            }

            if (!Enum.IsDefined(typeof(GateInfoDisplayMode), gateStructureDto.StatusDisplayMode))
            {
                return "Invalid StatusDisplayMode.";
            }

            if (gateStructureDto.AnimationDurationTicks.HasValue && gateStructureDto.AnimationDurationTicks <= 0)
            {
                return "AnimationDurationTicks must be greater than 0.";
            }

            if (gateStructureDto.HealthCurrent.HasValue && gateStructureDto.HealthMax.HasValue &&
                gateStructureDto.HealthCurrent > gateStructureDto.HealthMax)
            {
                return "HealthCurrent cannot exceed HealthMax.";
            }

            return null;
        }
    }
}
