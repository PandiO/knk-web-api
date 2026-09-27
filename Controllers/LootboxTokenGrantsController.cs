using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Lootbox token grant rules (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 5): which premium tiers and kits
    /// issue which token items. Web-app admin only.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequirePermission(StaffPermissions.ManageLootboxes)]
    public class LootboxTokenGrantsController : ControllerBase
    {
        private readonly ILootboxTokenGrantService _service;

        public LootboxTokenGrantsController(ILootboxTokenGrantService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<List<LootboxTokenGrantDto>>> GetAll()
        {
            return Ok(await _service.GetAllAsync());
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] LootboxTokenGrantDto dto)
        {
            if (dto == null) return BadRequest();
            return await LootboxResults.Run(this, async () =>
                StatusCode(StatusCodes.Status201Created, await _service.CreateAsync(dto)));
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] LootboxTokenGrantDto dto)
        {
            if (dto == null) return BadRequest();
            return await LootboxResults.Run(this, async () => Ok(await _service.UpdateAsync(id, dto)));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            return await LootboxResults.Run(this, async () =>
            {
                await _service.DeleteAsync(id);
                return NoContent();
            });
        }
    }
}
