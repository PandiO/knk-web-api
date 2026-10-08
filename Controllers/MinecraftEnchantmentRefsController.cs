using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace KnKWebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
// Closed-alpha WP1.3: every write (incl. POST search) needs knk.admin.content or the game server; reads stay logged-in.
[RequireServiceOrPermissionForWrites(StaffPermissions.ManageContent)]
public class MinecraftEnchantmentRefsController : ControllerBase
{
    private readonly IMinecraftEnchantmentRefService _service;

    public MinecraftEnchantmentRefsController(IMinecraftEnchantmentRefService service)
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
    public async Task<ActionResult<IEnumerable<MinecraftHybridEnchantmentOptionDto>>> GetHybrid(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] int? take = null)
    {
        var result = await _service.GetHybridAsync(search, category, take);
        return Ok(result);
    }

    [HttpGet("{id:int}", Name = "GetMinecraftEnchantmentRefById")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MinecraftEnchantmentRefCreateDto dto)
    {
        if (dto == null) return BadRequest();
        try
        {
            var created = await _service.CreateAsync(dto);
            return CreatedAtRoute("GetMinecraftEnchantmentRefById", new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("get-or-create")]
    public async Task<IActionResult> GetOrCreate([FromBody] MinecraftEnchantmentRefCreateDto dto)
    {
        if (dto == null) return BadRequest();
        try
        {
            var result = await _service.GetOrCreateAsync(dto.NamespaceKey, dto.Category, dto.LegacyName);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] MinecraftEnchantmentRefUpdateDto dto)
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
    public async Task<ActionResult<PagedResultDto<MinecraftEnchantmentRefListDto>>> Search([FromBody] PagedQueryDto? query)
    {
        query ??= new PagedQueryDto();

        if (query.Filters != null && query.Filters.TryGetValue("SearchHybrid", out var hybridValue) && hybridValue?.ToString()?.ToLower() == "true")
        {
            var hybridResult = await _service.SearchHybridAsync(query);
            return Ok(hybridResult);
        }

        var result = await _service.SearchAsync(query);
        return Ok(result);
    }
}
