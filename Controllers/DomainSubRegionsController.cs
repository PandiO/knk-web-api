using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// A domain's sub-regions (KNG-254, knk-workspace docs/specs/domain-subregions/DESIGN.md §4.2): added, edited and
    /// removed from the domain's form. Writes need knk.admin.world, like the domains themselves. Wrong world or a region
    /// another domain uses is 400 with the message.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireServiceOrPermissionForWrites(StaffPermissions.ManageWorld)]
    public class DomainSubRegionsController : ControllerBase
    {
        private readonly IDomainSubRegionService _service;

        public DomainSubRegionsController(IDomainSubRegionService service)
        {
            _service = service;
        }

        [HttpGet("by-domain/{domainId:int}")]
        public async Task<IActionResult> GetByDomain(int domainId)
        {
            return Ok(await _service.GetByDomainAsync(domainId));
        }

        [HttpGet("{id:int}", Name = nameof(GetDomainSubRegionById))]
        public async Task<IActionResult> GetDomainSubRegionById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DomainSubRegionDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(dto);
                return CreatedAtRoute(nameof(GetDomainSubRegionById), new { id = created.Id }, created);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DomainSubRegionDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                return Ok(await _service.UpdateAsync(id, dto));
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
    }
}
