using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
    public class MinecraftMaterialRefsController : ControllerBase
    {
        private readonly IMinecraftMaterialRefService _service;

        public MinecraftMaterialRefsController(IMinecraftMaterialRefService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("hybrid")]
        public async Task<ActionResult<IEnumerable<MinecraftHybridMaterialOptionDto>>> GetHybrid(
            [FromQuery] string? search = null,
            [FromQuery] string? category = null,
            [FromQuery] int? take = null)
        {
            var result = await _service.GetHybridAsync(search, category, take);
            return Ok(result);
        }

        [HttpGet("{id:int}", Name = "GetMinecraftMaterialRefById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] MinecraftMaterialRefCreateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute("GetMinecraftMaterialRefById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("get-or-create")]
        public async Task<IActionResult> GetOrCreate([FromBody] MinecraftMaterialRefCreateDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.GetOrCreateAsync(dto.NamespaceKey, dto.Category, dto.LegacyName);
                return Ok(created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] MinecraftMaterialRefUpdateDto dto)
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
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<MinecraftMaterialRefListDto>>> Search([FromBody] PagedQueryDto? query)
        {
            query ??= new PagedQueryDto();

            // Check if hybrid mode is requested via filter
            if (query.Filters != null && query.Filters.TryGetValue("SearchHybrid", out var hybridValue) && hybridValue?.ToString()?.ToLower() == "true")
            {
                var hybridResult = await _service.SearchHybridAsync(query);
                return Ok(hybridResult);
            }

            // Default: normal paged search
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }
    }
}
