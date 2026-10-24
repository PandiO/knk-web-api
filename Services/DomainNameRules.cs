using System;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Name and description rules shared by every domain type (Town, District, Structure, GateStructure) and by
    /// <c>PUT api/Domains/{id}</c>: both are trimmed, and a name that is empty after trimming is refused.
    /// </summary>
    /// <remarks>
    /// The dev database held districts named <c>"Residential District\n"</c>: the web app's String field is a
    /// textarea, so Enter added a newline the API stored as is, and the action bar rendered it as a missing glyph
    /// (<c>You are now entering Residential District []</c>). The plugin strips names defensively since; this is
    /// the fix at the source. Every create/update path calls <see cref="Normalize(IDomainNameDto, string, string)"/>
    /// or <see cref="Normalize(Domain, string)"/> before any other rule reads the name; <see cref="Domain.Name"/>'s
    /// setter trims as well (master <c>09cf7e4</c>), so an entity built on any other path never holds an untrimmed
    /// name, and the <c>TrimDomainNames</c> migration cleaned the rows that were already stored.
    /// </remarks>
    public static class DomainNameRules
    {
        /// <summary>
        /// Trims the DTO's <c>Name</c> and <c>Description</c> in place (leading/trailing whitespace, line breaks
        /// included). A null or empty description stays as it is.
        /// </summary>
        /// <param name="dto">The create/update payload.</param>
        /// <param name="entityLabel">"Town", "District", … for the error message.</param>
        /// <param name="paramName">The argument name reported on the exception.</param>
        /// <exception cref="ArgumentException">The name is null, empty or whitespace only (mapped to 400 by the controllers).</exception>
        public static void Normalize(IDomainNameDto dto, string entityLabel, string paramName)
        {
            if (dto == null) throw new ArgumentNullException(paramName);
            dto.Name = RequireName(dto.Name, entityLabel, paramName);
            dto.Description = dto.Description?.Trim()!;
        }

        /// <summary>Same as <see cref="Normalize(IDomainNameDto, string, string)"/> for a <see cref="Domain"/> sent as the payload.</summary>
        public static void Normalize(Domain domain, string paramName)
        {
            if (domain == null) throw new ArgumentNullException(paramName);
            domain.Name = RequireName(domain.Name, "Domain", paramName);
            domain.Description = domain.Description?.Trim()!;
        }

        /// <summary>The trimmed name; throws when nothing is left.</summary>
        public static string RequireName(string? name, string entityLabel, string paramName)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException($"{entityLabel} name is required.", paramName);
            return trimmed;
        }
    }
}
