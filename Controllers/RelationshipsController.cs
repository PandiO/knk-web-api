using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Select / Unlink / Add on the entity detail page (PandiO/knk-workspace#188): links an existing record to a
    /// relationship of a dashboard type, or removes the link, without editing anything else and without ever deleting
    /// the related record. Which relationships may change, and why the others can't, is in the type's metadata
    /// (<c>relationships</c>, from <see cref="RelationshipCatalog"/>). Access: <see cref="RequireRelationshipWriteAttribute"/>
    /// (the type's write node, plus the related type's when the related row changes; web users only).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [RequireRelationshipWrite]
    public class RelationshipsController : ControllerBase
    {
        private readonly IRelationshipService _relationships;

        public RelationshipsController(IRelationshipService relationships)
        {
            _relationships = relationships;
        }

        /// <summary>
        /// Select (a reference) or Add (a collection): <c>{ relatedId, move? }</c>. Linking what is already linked
        /// changes nothing (<c>changed: false</c>). A child that belongs to another parent answers 409
        /// <c>AlreadyLinked</c> with <c>currentId</c> until the request says <c>move: true</c>.
        /// </summary>
        /// <response code="200">{ entityType, id, property, relatedId, changed, previousId? }</response>
        /// <response code="400">Not editable here (with the reason), a cycle, or no related id</response>
        /// <response code="403">A write node is missing</response>
        /// <response code="404">Unknown type or relationship, or the record or related record doesn't exist</response>
        /// <response code="409">Already linked elsewhere, or the database refused the change</response>
        [HttpPost("{entityType}/{id:int}/{property}/link")]
        public Task<IActionResult> Link(string entityType, int id, string property, [FromBody] RelationshipLinkRequestDto? request) =>
            Run(() => _relationships.LinkAsync(entityType, id, property, request?.RelatedId ?? 0, request?.Move ?? false));

        /// <summary>
        /// Unlink: clears a reference or takes one record out of a collection (<c>{ relatedId }</c>, required for a
        /// collection). The related record stays. Unlinking what isn't linked: a reference changes nothing, a collection
        /// answers 404 <c>NotLinked</c>.
        /// </summary>
        /// <response code="200">{ entityType, id, property, relatedId, changed, previousId? }</response>
        /// <response code="400">Not editable here (with the reason), or no related id for a collection</response>
        /// <response code="403">A write node is missing</response>
        /// <response code="404">Unknown type, relationship or record, or not linked</response>
        [HttpPost("{entityType}/{id:int}/{property}/unlink")]
        public Task<IActionResult> Unlink(string entityType, int id, string property, [FromBody] RelationshipUnlinkRequestDto? request) =>
            Run(() => _relationships.UnlinkAsync(entityType, id, property, request?.RelatedId));

        private async Task<IActionResult> Run(Func<Task<RelationshipChangeResultDto>> change)
        {
            try
            {
                return Ok(await change());
            }
            catch (RelationshipException ex)
            {
                object body = ex.CurrentId is { } currentId
                    ? new { error = ex.Code, message = ex.Message, currentId }
                    : new { error = ex.Code, message = ex.Message };
                return new ObjectResult(body) { StatusCode = ex.Status };
            }
        }
    }
}
