using knkwebapi_v2.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.MinecraftCatalog;
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
            catch (MinecraftMaterialRefConflictException ex)
            {
                // KNG-200 phase 1: the key exists; the body names the row to use instead.
                return Conflict(new { message = ex.Message, id = ex.ExistingId });
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
            catch (MinecraftMaterialRefConflictException ex)
            {
                return Conflict(new { message = ex.Message, id = ex.ExistingId });
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

            // KNG-200: pickers send Picker=true (SearchHybrid=true before KNG-200); the database is the catalogue.
            if (MinecraftPickerFilters.IsPickerSearch(query.Filters))
            {
                return Ok(await _service.SearchPickerAsync(query));
            }

            var result = await _service.SearchAsync(query);
            return Ok(result);
        }
    }
}
