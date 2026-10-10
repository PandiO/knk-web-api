using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers;

/// <summary>
/// Shared error mapping of the road navigation controllers (docs/specs/navigation, plan Phase
/// 1.4): validation → 400 ValidationFailed, not found → 404 NotFound, a business conflict → 409
/// Conflict, all as <c>{ error, message }</c>.
/// </summary>
public abstract class RoadControllerBase : ControllerBase
{
    protected async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "NotFound", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "ValidationFailed", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "Conflict", message = ex.Message });
        }
    }

    protected IActionResult RequireWorld(string? world) =>
        string.IsNullOrWhiteSpace(world)
            ? BadRequest(new { error = "ValidationFailed", message = "world is required." })
            : Ok();
}
