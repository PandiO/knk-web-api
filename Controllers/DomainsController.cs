using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DomainsController : ControllerBase
    {
        private readonly IDomainService _service;
        private readonly IMapper _mapper;

        public DomainsController(IDomainService service, IMapper mapper)
        {
            _service = service;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:int}", Name = nameof(GetDomainById))]
        public async Task<IActionResult> GetDomainById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Domain domain)
        {
            if (domain == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(domain);
                return CreatedAtRoute(nameof(GetDomainById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Domain domain)
        {
            if (domain == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, domain);
                // Return the updated entity instead of 204
                var updated = await _service.GetByIdAsync(id);
                return Ok(updated);
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
            catch (DbUpdateException ex)
            {
                // Surfaced now that ItemBlueprintOrigin.DomainId (Restrict) is the first FK to ever
                // reference Domains - matches the DbUpdateException handling every other controller with
                // an incoming FK already has (CategoriesController, GradesController, etc.).
                return Conflict(new { code = "DbConstraint", message = ex.Message });
            }
        }

        [HttpGet("by-region/{regionName}")]
        public async Task<ActionResult<DomainRegionDecisionDto>> GetByRegionName(string regionName)
        {
            if (string.IsNullOrWhiteSpace(regionName)) return BadRequest("regionName is required.");
            var dto = await _service.GetByWgRegionNameAsync(regionName);
            if (dto == null) return NotFound();
            return Ok(dto);
        }

        [HttpPost("search")]
        public async Task<ActionResult<PagedResultDto<DomainListDto>>> Search([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }

        [HttpPost("search-region-decisions")]
        public async Task<ActionResult<Dictionary<int, DomainRegionDecisionDto>>> SearchDomainRegionDecision([FromBody] DomainRegionQueryDto queryDto)
        {
            if (queryDto.WgRegionIds == null) return BadRequest("WgRegionIds is required.");

            var result = await _service.SearchDomainRegionDecisionAsync(queryDto);
            return Ok(result);
        }
    }
}
