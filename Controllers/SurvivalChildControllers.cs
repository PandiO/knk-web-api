using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    /// <summary>
    /// Siege Survival phase 1 (PandiO/knk-workspace#41, link S4): the CRUD shape shared by every survival child
    /// collection. <c>GET by-parent/{parentId}</c> lists the rows of one parent (the scenario, or the owning
    /// profile / zone / point / pool - see each subclass), <c>GET {id}</c>, <c>POST</c> (the parent id in the body),
    /// <c>PUT {id}</c> (the parent can't change), <c>DELETE {id}</c>. Every subclass carries the class-level write
    /// rule <see cref="RequireServiceOrPermissionForWritesAttribute"/> with the Siege setup node, like the Siege
    /// scenario endpoints; reads stay "logged in or the game server".
    /// </summary>
    public abstract class SurvivalChildController<TRead, TUpsert> : ControllerBase where TUpsert : class
    {
        private readonly ISurvivalChildOperations<TRead, TUpsert> _ops;

        protected SurvivalChildController(ISurvivalChildOperations<TRead, TUpsert> ops)
        {
            _ops = ops;
        }

        [HttpGet("by-parent/{parentId:int}")]
        public async Task<IActionResult> ListByParent(int parentId) => Ok(await _ops.ListAsync(parentId));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var item = await _ops.GetAsync(id);
            return item == null ? NotFound() : Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TUpsert dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () =>
            {
                var created = await _ops.CreateAsync(dto);
                var id = created?.GetType().GetProperty("Id")?.GetValue(created);
                return Created($"/api/{ControllerContext.ActionDescriptor.ControllerName}/{id}", created);
            });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] TUpsert dto)
        {
            if (dto == null) return BadRequest();
            return await Run(async () =>
            {
                await _ops.UpdateAsync(id, dto);
                return NoContent();
            });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            return await Run(async () =>
            {
                await _ops.DeleteAsync(id);
                return NoContent();
            });
        }

        private static async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (KeyNotFoundException ex)
            {
                return new NotFoundObjectResult(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return new BadRequestObjectResult(ex.Message);
            }
            catch (InvalidOperationException ex) when (!DeleteConstraintExceptionFilter.IsConstraintFailure(ex))
            {
                return new ConflictObjectResult(new { code = "BusinessRuleViolation", message = ex.Message });
            }
        }
    }

    /// <summary>Difficulty profiles of a scenario (DESIGN §7); parent = the scenario.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalDifficultyProfilesController : SurvivalChildController<SurvivalDifficultyProfileDto, SurvivalDifficultyProfileUpsertDto>
    {
        public SurvivalDifficultyProfilesController(ISiegeSurvivalService service) : base(service.DifficultyProfiles) { }
    }

    /// <summary>The vanilla monster types of a scenario's normal waves (S42, S56); parent = the scenario.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalMonsterTypesController : SurvivalChildController<SurvivalMonsterTypeDto, SurvivalMonsterTypeUpsertDto>
    {
        public SurvivalMonsterTypesController(ISiegeSurvivalService service) : base(service.MonsterTypes) { }
    }

    /// <summary>Special-wave profiles (S14, S53); parent = the scenario.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalSpecialWaveProfilesController : SurvivalChildController<SurvivalSpecialWaveProfileDto, SurvivalSpecialWaveProfileUpsertDto>
    {
        public SurvivalSpecialWaveProfilesController(ISiegeSurvivalService service) : base(service.SpecialWaveProfiles) { }
    }

    /// <summary>The monster types of a special-wave profile; parent = the profile.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalSpecialWaveMonsterTypesController : SurvivalChildController<SurvivalSpecialWaveMonsterTypeDto, SurvivalSpecialWaveMonsterTypeUpsertDto>
    {
        public SurvivalSpecialWaveMonsterTypesController(ISiegeSurvivalService service) : base(service.SpecialWaveMonsterTypes) { }
    }

    /// <summary>The special-wave schedule entries (recurring and explicit); parent = the scenario.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalSpecialWaveEntriesController : SurvivalChildController<SurvivalSpecialWaveEntryDto, SurvivalSpecialWaveEntryUpsertDto>
    {
        public SurvivalSpecialWaveEntriesController(ISiegeSurvivalService service) : base(service.SpecialWaveEntries) { }
    }

    /// <summary>NPC spawnzones (DESIGN §6); parent = the scenario.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalSpawnZonesController : SurvivalChildController<SurvivalSpawnZoneDto, SurvivalSpawnZoneUpsertDto>
    {
        public SurvivalSpawnZonesController(ISiegeSurvivalService service) : base(service.SpawnZones) { }
    }

    /// <summary>A zone's spawnpoints and ordered lane-override points; parent = the spawnzone.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalSpawnZonePointsController : SurvivalChildController<SurvivalSpawnZonePointDto, SurvivalSpawnZonePointUpsertDto>
    {
        public SurvivalSpawnZonePointsController(ISiegeSurvivalService service) : base(service.SpawnZonePoints) { }
    }

    /// <summary>NPC targeting overrides per objective and gate (DESIGN §4); parent = the scenario.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalTargetOverridesController : SurvivalChildController<SurvivalTargetOverrideDto, SurvivalTargetOverrideUpsertDto>
    {
        public SurvivalTargetOverridesController(ISiegeSurvivalService service) : base(service.TargetOverrides) { }
    }

    /// <summary>Fixed purchase points (DESIGN §8); parent = the scenario.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalPurchasePointsController : SurvivalChildController<SurvivalPurchasePointDto, SurvivalPurchasePointUpsertDto>
    {
        public SurvivalPurchasePointsController(ISiegeSurvivalService service) : base(service.PurchasePoints) { }
    }

    /// <summary>The offers of a purchase point; parent = the purchase point.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalPurchaseOffersController : SurvivalChildController<SurvivalPurchaseOfferDto, SurvivalPurchaseOfferUpsertDto>
    {
        public SurvivalPurchaseOffersController(ISiegeSurvivalService service) : base(service.PurchaseOffers) { }
    }

    /// <summary>Mystery Box pools (DESIGN §8a); parent = the scenario. A pool a box point uses can't be deleted (409).</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalMysteryBoxPoolsController : SurvivalChildController<SurvivalMysteryBoxPoolDto, SurvivalMysteryBoxPoolUpsertDto>
    {
        public SurvivalMysteryBoxPoolsController(ISiegeSurvivalService service) : base(service.MysteryBoxPools) { }
    }

    /// <summary>The entries of a Mystery Box pool with their chances; parent = the pool.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalMysteryBoxEntriesController : SurvivalChildController<SurvivalMysteryBoxEntryDto, SurvivalMysteryBoxEntryUpsertDto>
    {
        public SurvivalMysteryBoxEntriesController(ISiegeSurvivalService service) : base(service.MysteryBoxEntries) { }
    }

    /// <summary>Mystery Box points in the world (DESIGN §8a); parent = the scenario.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.SiegeManage)]
    public class SurvivalMysteryBoxPointsController : SurvivalChildController<SurvivalMysteryBoxPointDto, SurvivalMysteryBoxPointUpsertDto>
    {
        public SurvivalMysteryBoxPointsController(ISiegeSurvivalService service) : base(service.MysteryBoxPoints) { }
    }
}
