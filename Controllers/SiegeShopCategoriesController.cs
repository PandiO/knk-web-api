using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Siege shop (knk-workspace docs/specs/siege-shop/DESIGN.md § 6.1.1, § 6.2.1; PandiO/knk-workspace#867): shop
    // category CRUD. A category with entries can't be deleted (409).
    [ApiController]
    [Route("api/[controller]")]
    [DashboardReads("SiegeShopCategory")]
    public class SiegeShopCategoriesController : ControllerBase
    {
        private readonly ISiegeShopService _service;

        public SiegeShopCategoriesController(ISiegeShopService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetCategoriesAsync());

        [HttpGet("{id:int}", Name = "GetSiegeShopCategoryById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetCategoryByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("SiegeShopCategory")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SiegeShopCategoryUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateCategoryAsync(dto);
                return CreatedAtRoute("GetSiegeShopCategoryById", new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }

        [RequireDashboardChange("SiegeShopCategory")]
        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SiegeShopCategoryUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateCategoryAsync(id, dto);
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
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }

        [RequireServiceOrPermission(StaffPermissions.SiegeManage)]
        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280).
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _service.DeleteCategoryAsync(id);
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

        [RequireServiceOrLoggedIn] // a read sent as POST, like the GET list
        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<SiegeShopCategoryReadDto>>> Search([FromBody] PagedQueryDto query)
        {
            return Ok(await _service.SearchCategoriesAsync(query));
        }
    }
}
