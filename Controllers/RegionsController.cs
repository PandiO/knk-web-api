using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Services;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RegionsController : ControllerBase
    {
        private readonly IRegionService _regionService;
        private readonly IDomainRegionNameFinalizer _regionNames;

        private readonly ILogger<RegionsController> _logger;

        public RegionsController(IRegionService regionService, IDomainRegionNameFinalizer regionNames, ILogger<RegionsController>? logger = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RegionsController>.Instance;
            _regionService = regionService;
            _regionNames = regionNames;
        }

        /// <summary>
        /// Renames every domain region that still has a temporary name (tempregion_worldtask_&lt;n&gt;) to domain_&lt;id&gt;
        /// and has the plugin set it up as its domain type. For domains created before every type renamed on submit
        /// (KNG-43), or whose rename failed because the plugin was offline. Needs the Minecraft server running; safe to repeat.
        /// </summary>
        [HttpPost("finalize-temp-names")]
        [RequireServiceOrPermission(StaffPermissions.ManageRegions)]
        public async Task<ActionResult<TempRegionFinalizeResult>> FinalizeTempRegionNames()
        {
            return Ok(await _regionNames.FinalizeAllAsync());
        }

        /// <summary>
        /// Rename a WorldGuard region.
        /// This endpoint is called by the Minecraft plugin after an entity is successfully created/updated
        /// to finalize the temporary region name to the actual formatted name.
        /// </summary>
        /// <param name="oldRegionId">The current/temporary region ID</param>
        /// <param name="newRegionId">The desired new region ID</param>
        /// <param name="domainType">Optional concrete domain type (Town, District, Structure, GateStructure): the plugin then also applies the managed-region parent, priority and flags</param>
        /// <param name="parentRegionId">Optional WorldGuard region ID of the domain's parent (District's Town, Structure's District)</param>
        /// <returns>true if successful, false otherwise</returns>
        [RequireServiceOrPermission(StaffPermissions.ManageRegions)]
        [HttpPost("rename")]
        public async Task<ActionResult<bool>> RenameRegion([FromQuery] string oldRegionId, [FromQuery] string newRegionId, [FromQuery] string? domainType = null, [FromQuery] string? parentRegionId = null)
        {
            if (string.IsNullOrWhiteSpace(oldRegionId) || string.IsNullOrWhiteSpace(newRegionId))
            {
                return BadRequest("oldRegionId and newRegionId are required.");
            }

            try
            {
                var result = await _regionService.RenameRegionAsync(oldRegionId, newRegionId, domainType, parentRegionId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Renaming region {OldRegionId} to {NewRegionId} failed", oldRegionId, newRegionId);
                return StatusCode(500, "Error renaming region.");
            }
        }
    }
}
