using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnKWebAPI.Controllers
{
    // Player harvesting P8a (PandiO/knk-workspace#685, PRODUCTION_KINDS.md § 6-7). Admin CRUD works while
    // Production:Enabled is off (DESIGN § 18.2); harvest reports are 409 and the catalogue is empty while it is off (A21).

    /// <summary>Harvest rules per production kind: reads the World area (or the plugin key); writes knk.admin.world (+ the
    /// dashboard change rule); delete also knk.admin.delete. <c>GET {id}/balance</c> is the net-output check (F9).</summary>
    [ApiController]
    [Route("api/HarvestRules")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("HarvestRule")]
    public class HarvestRulesController : ControllerBase
    {
        private readonly IHarvestRuleService _service;

        public HarvestRulesController(IHarvestRuleService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetHarvestRuleById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [RequireDashboardChange("HarvestRule")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] HarvestRuleDto dto) => HarvestErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateAsync(dto);
            return CreatedAtRoute("GetHarvestRuleById", new { id = created.Id }, created);
        });

        [RequireDashboardChange("HarvestRule")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] HarvestRuleDto dto) => HarvestErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            await _service.UpdateAsync(id, dto);
            return Ok(await _service.GetByIdAsync(id));
        });

        // Area node AND knk.admin.delete (dashboard access model, PandiO/knk-workspace#280). 409 once it has harvests.
        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id) => HarvestErrors.Run(async () =>
        {
            await _service.DeleteAsync(id);
            return NoContent();
        });

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<HarvestRuleListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));

        /// <summary>The town's share of a break against the production it costs until the pool refills, per area of the
        /// rule's kind (PRODUCTION_KINDS § 6.3, F9); <c>netPositive</c> false and <c>warnings</c> where it is not.</summary>
        [HttpGet("{id:int}/balance")]
        public Task<IActionResult> Balance(int id, CancellationToken ct) =>
            HarvestErrors.Run(async () => Ok(await _service.BalanceAsync(id, ct)));
    }

    /// <summary>Resource pools, one per production area: reads the World area (or the plugin key); writes knk.admin.world
    /// (+ the dashboard change rule); delete also knk.admin.delete.</summary>
    [ApiController]
    [Route("api/ResourcePools")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("ResourcePool")]
    public class ResourcePoolsController : ControllerBase
    {
        private readonly IResourcePoolService _service;

        public ResourcePoolsController(IResourcePoolService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

        [HttpGet("{id:int}", Name = "GetResourcePoolById")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        /// <summary>Makes an area's pool (409 when it has one): capacity from the latest survey unless given.</summary>
        [RequireDashboardChange("ResourcePool")]
        [HttpPost]
        public Task<IActionResult> Create([FromBody] ResourcePoolDto dto) => HarvestErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            var created = await _service.CreateAsync(dto);
            return CreatedAtRoute("GetResourcePoolById", new { id = created.Id }, created);
        });

        /// <summary>Capacity, units left, regeneration per hour, capacity from surveys. Settles the structure first.</summary>
        [RequireDashboardChange("ResourcePool")]
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] ResourcePoolDto dto) => HarvestErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            await _service.UpdateAsync(id, dto);
            return Ok(await _service.GetByIdAsync(id));
        });

        [RequireServiceOrPermission(StaffPermissions.Delete)]
        [HttpDelete("{id:int}")]
        public Task<IActionResult> Delete(int id) => HarvestErrors.Run(async () =>
        {
            await _service.DeleteAsync(id);
            return NoContent();
        });

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<ResourcePoolListDto>>> Search([FromBody] PagedQueryDto query) =>
            Ok(await _service.SearchAsync(query));
    }

    /// <summary>The booked harvests (read only): the World area, or the plugin key.</summary>
    [ApiController]
    [Route("api/PlayerHarvests")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    [DashboardReads("PlayerHarvest")]
    public class PlayerHarvestsController : ControllerBase
    {
        private readonly IHarvestService _service;

        public PlayerHarvestsController(IHarvestService service) => _service = service;

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id, CancellationToken ct)
        {
            var item = await _service.GetAsync(id, ct);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<PlayerHarvestListDto>>> Search([FromBody] PagedQueryDto query, CancellationToken ct) =>
            Ok(await _service.SearchAsync(query, ct));
    }

    /// <summary>
    /// The game server's harvest endpoints (PRODUCTION_KINDS § 6):
    /// <list type="number">
    ///   <item><c>GET catalog</c>: the enabled rules and the areas they apply to (the World area, or the plugin key);
    ///   empty with <c>featureEnabled: false</c> while <c>Production:Enabled</c> is off.</item>
    ///   <item><c>POST reports</c>: the game server only; books each entry once per idempotency key and answers how many
    ///   units the player gets and when the block comes back. 409 <c>ProductionDisabled</c> while the feature is off.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/Harvest")]
    public class HarvestController : ControllerBase
    {
        private readonly IHarvestService _service;

        public HarvestController(IHarvestService service) => _service = service;

        [DashboardReads("HarvestRule")]
        [HttpGet("catalog")]
        public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(await _service.CatalogAsync(ct));

        [RequirePluginService]
        [HttpPost("reports")]
        public Task<IActionResult> Report([FromBody] HarvestReportDto dto, CancellationToken ct) => HarvestErrors.Run(async () =>
        {
            if (dto == null) return BadRequest();
            return Ok(await _service.ReportAsync(dto, ct));
        });
    }

    internal static class HarvestErrors
    {
        public static async Task<IActionResult> Run(Func<Task<IActionResult>> work)
        {
            try
            {
                return await work();
            }
            catch (KeyNotFoundException ex)
            {
                return new NotFoundObjectResult(new { error = ex.Message });
            }
            catch (ProductionOffException ex)
            {
                return new ConflictObjectResult(new { code = "ProductionDisabled", error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return new ConflictObjectResult(new { error = ex.Message });
            }
            catch (DbUpdateException)
            {
                return new ConflictObjectResult(new { error = "The data changed while saving; try again (a harvest report with the same keys)." });
            }
        }
    }
}
