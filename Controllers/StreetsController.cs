using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Dtos;

namespace KnKWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StreetsController : ControllerBase
    {
        private readonly IStreetService _service;
        private readonly IRoadNetworkService _roads;

        public StreetsController(IStreetService service, IRoadNetworkService roads)
        {
            _service = service;
            _roads = roads;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _service.GetAllAsync();
            return Ok(items);
        }

        [HttpGet("{id:int}", Name = nameof(GetStreetById))]
        public async Task<IActionResult> GetStreetById(int id)
        {
            var item = await _service.GetByIdAsync(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        /// <summary>The street's road: its labelled edges and their nodes (docs/specs/navigation/DESIGN.md §3.7).</summary>
        [HttpGet("{id:int}/road")]
        [ProducesResponseType(typeof(StreetRoadDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetStreetRoad(int id)
        {
            var road = await _roads.GetStreetRoadAsync(id);
            if (road == null) return NotFound();
            return Ok(road);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StreetDto streetDto)
        {
            if (streetDto == null) return BadRequest();
            try
            {
                var created = await _service.CreateAsync(streetDto);
                return CreatedAtRoute(nameof(GetStreetById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] StreetDto streetDto)
        {
            if (streetDto == null) return BadRequest();
            try
            {
                await _service.UpdateAsync(id, streetDto);
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
        public async Task<ActionResult<PagedResultDto<StreetListDto>>> SearchStreets([FromBody] PagedQueryDto query)
        {
            var result = await _service.SearchAsync(query);
            return Ok(result);
        }
    }
}
