using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// Territory V1, the game server contract and the per-town and per-structure views (territory DESIGN § 1-4,
    /// PandiO/knk-workspace#326):
    /// <list type="number">
    ///   <item><c>GET inputs</c>: everything the chunk cache is built from; the game server reads it at start and after
    ///     a <c>TerritoryChanged</c> realtime event.</item>
    ///   <item><c>PUT report</c>: the game server's overlaps and structure flags for one world (only with
    ///     <c>Production:Enabled</c> on; 409 otherwise).</item>
    ///   <item><c>GET towns/{id}</c>: a town's power and effective ranges; <c>structures/{id}</c>: loyalty and flag.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads(DashboardArea.World)]
    public class TerritoryController : ControllerBase
    {
        private readonly ITerritoryService _service;

        public TerritoryController(ITerritoryService service)
        {
            _service = service;
        }

        [HttpGet("inputs")]
        public async Task<ActionResult<TerritoryInputsDto>> GetInputs() => Ok(await _service.GetInputsAsync());

        [RequirePluginService]
        [HttpPut("report")]
        public async Task<IActionResult> Report([FromBody] TerritoryReportDto report)
        {
            if (report == null) return BadRequest();
            try
            {
                return Ok(await _service.ApplyReportAsync(report));
            }
            catch (ProductionDisabledException ex)
            {
                return Conflict(new { error = ex.Message, code = "ProductionDisabled" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("towns/{id:int}")]
        public async Task<IActionResult> GetTown(int id)
        {
            var item = await _service.GetTownTerritoryAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpGet("structures/{id:int}")]
        public async Task<IActionResult> GetStructure(int id)
        {
            var item = await _service.GetStructureTerritoryAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Set the town a structure is loyal to (null = its own town). Stored only in V1.</summary>
        [HttpPut("structures/{id:int}/loyalty")]
        public async Task<IActionResult> SetLoyalty(int id, [FromBody] StructureLoyaltyUpdateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await _service.SetLoyaltyAsync(id, dto));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Clear a structure's territory flag by hand (the next report sets it again if it still applies).</summary>
        [HttpDelete("structures/{id:int}/flag")]
        public async Task<IActionResult> ClearFlag(int id)
        {
            try
            {
                return Ok(await _service.ClearFlagAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}
