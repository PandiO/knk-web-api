using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Lookup of one minted item (docs/specs/lootboxes/DESIGN.md §3.3): an admin reads the id from an
    /// item's PDC (<c>knightsandkings:knk_item_instance</c>) and sees its enchantments, owner and origin. There
    /// are deliberately no create/update/delete endpoints - services mint instances. The one write is the game
    /// server's <c>clone</c> for <c>/knk item more</c> (PandiO/knk-workspace#175).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ItemInstancesController : ControllerBase
    {
        private readonly IItemInstanceService _service;

        public ItemInstancesController(IItemInstanceService service)
        {
            _service = service;
        }

        [HttpGet("{id:long}")]
        [RequirePermission(StaffPermissions.ManageLootboxes)]
        public async Task<IActionResult> GetById(long id)
        {
            var item = await _service.GetAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        /// <summary>
        /// <c>/knk item more</c> (PandiO/knk-workspace#175): mint <c>count</c> new instances copied from this one, for
        /// the copies the plugin hands out (each copy gets its own id). Game server only; the staff member comes
        /// from X-Acting-User-Id and is audited (ItemInstanceCloned). 404 unknown instance/owner, 400 bad count,
        /// 409 when the blueprint is stackable now.
        /// </summary>
        [HttpPost("{id:long}/clone")]
        [RequirePluginService]
        public async Task<IActionResult> Clone(long id, [FromBody] ItemInstanceCloneRequestDto request)
        {
            if (request == null) return BadRequest();
            try
            {
                return Ok(await _service.CloneAsync(id, request.Count, request.OwnerUserId, HttpContext.GetKnkCaller().ActorUserId));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "Stackable", message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
