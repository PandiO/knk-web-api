using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.EnchantmentInfo;
using knkwebapi_v2.Dtos;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    [DashboardReads("EnchantmentDefinition")]
    public class EnchantmentDefinitionsController : ControllerBase
    {
        private readonly IEnchantmentDefinitionService _service;
        private readonly IEnchantmentInfoService? _info;

        public EnchantmentDefinitionsController(IEnchantmentDefinitionService service, IEnchantmentInfoService? info = null)
        {
            _service = service;
            _info = info;
        }

        /// <summary>
        /// PandiO/knk-workspace#221: the info sheets of every custom enchantment and ability for the public Guides page:
        /// what it does, whom it hits, its effects, damage, chance and cooldown per level, from the live combat values.
        /// Only the custom definitions, and only what players may know (no admin texts or ids).
        /// </summary>
        // Public by design (#221, like the #174 match pages): pinned in EndpointAccessRulesTests.
        [AllowAnonymous]
        [HttpGet("public/info")]
        [ProducesResponseType(typeof(List<EnchantmentInfoDto>), 200)]
        public async Task<IActionResult> PublicInfo()
        {
            if (_info == null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Ok(await _info.GetCustomEnchantmentInfoAsync());
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:int}", Name = "GetEnchantmentDefinitionById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [RequireDashboardChange("EnchantmentDefinition")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] EnchantmentDefinitionCreateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetEnchantmentDefinitionById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [RequireDashboardChange("EnchantmentDefinition")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] EnchantmentDefinitionUpdateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, dto);
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

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280).
        [RequireServiceOrPermission(StaffPermissions.Delete)]
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
            catch (InvalidOperationException ex) when (!DeleteConstraintExceptionFilter.IsConstraintFailure(ex))
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<EnchantmentDefinitionListDto>>> Search([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }
    }
}
