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
    // Doors of a GateStructure - introduced by item 5's multi-door support (see
    // docs/features/gate-structure-animation/GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md). Listing
    // and creation are scoped under the parent structure; individual door operations address the
    // door directly by its own id, mirroring GateStructuresController's shape.
    [ApiController]
    [Route("api")]
    public class GateDoorsController : ControllerBase
    {
        private readonly IGateDoorService _service;

        public GateDoorsController(IGateDoorService service)
        {
            _service = service;
        }

        [HttpGet("GateStructures/{gateStructureId:int}/doors")]
        public async Task<IActionResult> GetByStructure(int gateStructureId)
        {
            if (gateStructureId <= 0) return BadRequest("Invalid gateStructureId.");
            try
            {
                var items = await _service.GetByStructureIdAsync(gateStructureId);
                return Ok(items);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("GateDoors/{id:int}", Name = "GetGateDoorById")]
        public async Task<IActionResult> GetById(int id, [FromQuery] bool includeSnapshots = false)
        {
            var item = includeSnapshots
                ? await _service.GetByIdWithSnapshotsAsync(id)
                : await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost("GateStructures/{gateStructureId:int}/doors")]
        public async Task<IActionResult> Create(int gateStructureId, [FromBody] GateDoorDto gateDoorDto)
        {
            if (gateDoorDto == null) return BadRequest(ModelState);

            var validationError = ValidateDoorPayload(gateDoorDto);
            if (!string.IsNullOrWhiteSpace(validationError))
                return BadRequest(validationError);

            try
            {
                var created = await _service.CreateAsync(gateStructureId, gateDoorDto);
                return CreatedAtRoute("GetGateDoorById", new { id = created.Id }, created);
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

        [HttpPut("GateDoors/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] GateDoorDto gateDoorDto)
        {
            if (gateDoorDto == null) return BadRequest(ModelState);

            var validationError = ValidateDoorPayload(gateDoorDto);
            if (!string.IsNullOrWhiteSpace(validationError))
                return BadRequest(validationError);

            try
            {
                await _service.UpdateAsync(id, gateDoorDto);
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

        [HttpDelete("GateDoors/{id:int}")]
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

        [HttpPut("GateDoors/{id:int}/state")]
        public async Task<IActionResult> UpdateState(int id, [FromBody] GateDoorStateUpdateDto request)
        {
            if (id <= 0) return BadRequest("Invalid id.");
            if (request == null) return BadRequest();
            if (!Enum.IsDefined(typeof(GateDoorOpenState), request.OpenedState))
                return BadRequest("Invalid OpenedState.");

            try
            {
                await _service.UpdateStateAsync(id, request.OpenedState, request.IsDestroyed);
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

        [HttpPut("GateDoors/{id:int}/health")]
        public async Task<IActionResult> UpdateHealth(int id, [FromBody] GateDoorHealthUpdateDto request)
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

        [HttpPut("GateDoors/{id:int}/operational-settings")]
        public async Task<IActionResult> UpdateOperationalSettings(int id, [FromBody] GateDoorOperationalSettingsUpdateDto request)
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

        [HttpPut("GateDoors/{id:int}/region")]
        public async Task<IActionResult> UpdateRegionData(int id, [FromBody] GateDoorRegionUpdateDto request)
        {
            if (id <= 0) return BadRequest("Invalid id.");
            if (request == null) return BadRequest();

            try
            {
                await _service.UpdateRegionDataAsync(id, request.IsOpenedRegion, request.RegionData);
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

        [HttpGet("GateDoors/{id:int}/snapshots")]
        public async Task<IActionResult> GetSnapshots(int id)
        {
            if (id <= 0) return BadRequest("Invalid gateDoorId.");
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

        [HttpPost("GateDoors/{id:int}/snapshots/bulk")]
        public async Task<IActionResult> AddSnapshots(int id, [FromBody] IEnumerable<GateBlockSnapshotCreateDto> snapshots)
        {
            if (id <= 0) return BadRequest("Invalid gateDoorId.");
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

        [HttpDelete("GateDoors/{id:int}/snapshots")]
        public async Task<IActionResult> ClearSnapshots(int id)
        {
            if (id <= 0) return BadRequest("Invalid gateDoorId.");
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
        [HttpGet("GateDoors/{id:int}/openedSnapshots")]
        public async Task<IActionResult> GetOpenedSnapshots(int id)
        {
            if (id <= 0) return BadRequest("Invalid gateDoorId.");
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

        [HttpPost("GateDoors/{id:int}/openedSnapshots/bulk")]
        public async Task<IActionResult> AddOpenedSnapshots(int id, [FromBody] IEnumerable<GateOpenedBlockSnapshotCreateDto> snapshots)
        {
            if (id <= 0) return BadRequest("Invalid gateDoorId.");
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

        [HttpDelete("GateDoors/{id:int}/openedSnapshots")]
        public async Task<IActionResult> ClearOpenedSnapshots(int id)
        {
            if (id <= 0) return BadRequest("Invalid gateDoorId.");
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

        private static string? ValidateDoorPayload(GateDoorDto gateDoorDto)
        {
            if (string.IsNullOrWhiteSpace(gateDoorDto.Name))
            {
                return "GateDoor name is required.";
            }

            if (!Enum.IsDefined(typeof(GateFaceDirection), gateDoorDto.FaceDirection))
            {
                return "Invalid FaceDirection.";
            }

            if (!Enum.IsDefined(typeof(GateType), gateDoorDto.GateType))
            {
                return "Invalid GateType.";
            }

            if (!Enum.IsDefined(typeof(MotionType), gateDoorDto.MotionType))
            {
                return "Invalid MotionType.";
            }

            if (!Enum.IsDefined(typeof(GeometryDefinitionMode), gateDoorDto.GeometryDefinitionMode))
            {
                return "Invalid GeometryDefinitionMode.";
            }

            if (!Enum.IsDefined(typeof(TileEntityPolicy), gateDoorDto.TileEntityPolicy))
            {
                return "Invalid TileEntityPolicy.";
            }

            if (!Enum.IsDefined(typeof(GateDoorOpenState), gateDoorDto.OpenedState))
            {
                return "Invalid OpenedState.";
            }

            if (!Enum.IsDefined(typeof(HealthDisplayMode), gateDoorDto.HealthDisplayMode))
            {
                return "Invalid HealthDisplayMode.";
            }

            if (!Enum.IsDefined(typeof(GateInfoDisplayMode), gateDoorDto.GateNameDisplayMode))
            {
                return "Invalid GateNameDisplayMode.";
            }

            if (!Enum.IsDefined(typeof(GateInfoDisplayMode), gateDoorDto.StatusDisplayMode))
            {
                return "Invalid StatusDisplayMode.";
            }

            if (!Enum.IsDefined(typeof(GateInfoDisplayMode), gateDoorDto.DoorNameDisplayMode))
            {
                return "Invalid DoorNameDisplayMode.";
            }

            if (gateDoorDto.AnimationDurationTicks.HasValue && gateDoorDto.AnimationDurationTicks <= 0)
            {
                return "AnimationDurationTicks must be greater than 0.";
            }

            if (gateDoorDto.HealthCurrent.HasValue && gateDoorDto.HealthMax.HasValue &&
                gateDoorDto.HealthCurrent > gateDoorDto.HealthMax)
            {
                return "HealthCurrent cannot exceed HealthMax.";
            }

            return null;
        }
    }
}
