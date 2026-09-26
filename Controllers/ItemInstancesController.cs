using knkwebapi_v2.Attributes;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Read-only lookup of one minted item (docs/specs/lootboxes/DESIGN.md §3.3): an admin reads the id from an
    /// item's PDC (<c>knightsandkings:knk_item_instance</c>) and sees its enchantments, owner and origin. There
    /// are deliberately no create/update/delete endpoints - services mint instances.
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
    }
}
