using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RegionsController : ControllerBase
    {
        private readonly IRegionService _regionService;

        public RegionsController(IRegionService regionService)
        {
            _regionService = regionService;
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
                return StatusCode(500, $"Error renaming region: {ex.Message}");
            }
        }
    }
}
