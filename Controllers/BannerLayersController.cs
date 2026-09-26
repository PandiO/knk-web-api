using Microsoft.AspNetCore.Mvc;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;

namespace KnKWebAPI.Controllers
{
    // Single-layer operations. Listing and creation live under the parent banner
    // (GET/POST /api/BannerDesigns/{id}/layers), same split as GateDoors.
    [ApiController]
    [Route("api/[controller]")]
    public class BannerLayersController : ControllerBase
    {
        private readonly IBannerDesignService _service;

        public BannerLayersController(IBannerDesignService service)
        {
            _service = service;
        }

        [HttpGet("{id:int}", Name = "GetBannerLayerById")]
        public async Task<IActionResult> GetById(int id)
        {
            var layer = await _service.GetLayerByIdAsync(id);
            return layer == null ? NotFound() : Ok(layer);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] BannerLayerUpsertDto dto)
        {
            if (dto == null) return BadRequest();
            try
            {
                await _service.UpdateLayerAsync(id, dto);
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
                await _service.DeleteLayerAsync(id);
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
