using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace knkwebapi_v2.Services
{
    public interface IEntityReferenceService
    {
        /// <summary>
        /// What still points at one row: every foreign key in the model that targets the entity's
        /// type (many-to-many join rows included), with the number of rows using this id. Null when
        /// the entity type is unknown. A referencing row that is (or, for a join row, links to) one of
        /// <paramref name="ignore"/> ("Type:id") doesn't count: the web app passes the entities it is
        /// about to delete in the same run, so a Street and the District created inside it don't
        /// block each other.
        /// With <paramref name="blockingOnly"/> (KNG-172) only the references that stop a delete are
        /// listed (see <see cref="EntityReferenceService.BlocksDelete"/>): what the refusal message
        /// of a failed DELETE names. The default lists every reference, cascades included.
        /// </summary>
        Task<EntityReferencesDto?> GetReferencesAsync(string entityTypeName, string id, IEnumerable<string>? ignore = null, bool blockingOnly = false);
    }

    /// <summary>
    /// KNG-162: the "in use" check before the web app deletes an entity a child form created while a
    /// main form was filled out (decision D2: anything referenced elsewhere is skipped and reported,
    /// never force-deleted). Read-only and generic over the EF model, so it needs no per-entity code.
    ///
    /// Every referencing row counts, whatever the foreign key's delete behaviour: a cascade would
    /// remove the referencing row along with the entity, which is exactly the silent loss D2 rules
    /// out. Owned types are part of their owner and don't count. The check errs towards "in use"
    /// (keeping the entity), which is the reversible side. Only the refusal message of a failed
    /// DELETE asks for the blocking references alone (blockingOnly, KNG-172).
    /// </summary>
    public class EntityReferenceService : IEntityReferenceService
    {
        private static readonly MethodInfo ListMethod = typeof(EntityReferenceService)
            .GetMethod(nameof(ReferencingValuesAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        private readonly KnKDbContext _context;

        public EntityReferenceService(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<EntityReferencesDto?> GetReferencesAsync(string entityTypeName, string id, IEnumerable<string>? ignore = null, bool blockingOnly = false)
        {
            var principal = FindEntityType(entityTypeName);
            if (principal == null) return null;

            var key = principal.FindPrimaryKey();
            if (key == null || key.Properties.Count != 1) return null;
            var keyValue = ConvertKey(id, key.Properties[0].ClrType);
            if (keyValue == null) return null;

            var ignored = ParseIgnore(ignore);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var foreignKey in CountedForeignKeys(principal))
            {
                if (blockingOnly && !BlocksDelete(foreignKey)) continue;
                var dependent = foreignKey.DeclaringEntityType;

                // What identifies the referencing row for the ignore list: the other side of a join
                // row, else the referencing entity itself.
                var (identityType, identityProperty) = ReferencingIdentity(dependent, foreignKey);
                var values = await ReferencingRowsAsync(dependent, foreignKey.Properties[0], keyValue, identityProperty);
                var count = values.Count(value =>
                    identityType == null || value == null || !ignored.Contains(IgnoreKey(identityType, value)));
                if (count <= 0) continue;

                var label = ReferenceLabel(dependent, foreignKey);
                counts[label] = counts.TryGetValue(label, out var existing) ? existing + count : count;
            }

            return new EntityReferencesDto
            {
                EntityTypeName = principal.ClrType.Name,
                Id = id,
                InUse = counts.Count > 0,
                References = counts
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new EntityReferenceCountDto { ReferencedBy = entry.Key, Count = entry.Value })
                    .ToList()
            };
        }

        /// <summary>
        /// The foreign keys whose rows count as uses of <paramref name="principal"/>: every one that
        /// targets it or a base type, except ownerships, composite keys, owned dependents and the
        /// table-per-type links between one entity's own tables (<c>District.Id</c> → <c>Domain.Id</c>
        /// is the District's own row, so without this every District, Town and Structure was "in use"
        /// by itself).
        /// </summary>
        public static IEnumerable<IForeignKey> CountedForeignKeys(IEntityType principal) =>
            principal.GetReferencingForeignKeys().Where(foreignKey =>
                !foreignKey.IsOwnership
                && foreignKey.Properties.Count == 1
                && !foreignKey.DeclaringEntityType.IsOwned()
                && !IsInheritanceLink(foreignKey));

        /// <summary>
        /// References a delete endpoint clears itself before deleting, so they never stop it:
        /// FormConfigurationRepository.DeleteAsync detaches the form sessions started with the
        /// configuration (KNG-172).
        /// </summary>
        private static readonly HashSet<string> ClearedByDelete = new(StringComparer.Ordinal)
        {
            "WorkflowSession.FormConfigurationId",
        };

        /// <summary>
        /// KNG-172: whether rows of this foreign key stop the principal from being deleted. The rule
        /// follows what the database does, because the rows counted here are database rows, most of
        /// them not loaded when the delete runs:
        /// - Cascade (ON DELETE CASCADE) and SetNull (ON DELETE SET NULL): the database removes or
        ///   detaches the rows itself, so they don't block.
        /// - Restrict and NoAction: the database refuses the delete. Blocks.
        /// - ClientSetNull, ClientCascade and ClientNoAction: EF creates these as NO ACTION in the
        ///   database and only nulls/deletes the dependents it has loaded; rows it hasn't loaded make
        ///   the database refuse. Blocks.
        /// - References the delete endpoint clears itself (<see cref="ClearedByDelete"/>) don't block.
        /// Not followed: a cascaded row that is itself held by a restricting row further down (a draft
        /// whose child draft points at it); the delete endpoint has to clear those itself, as
        /// FormConfigurationRepository.DeleteAsync does for child drafts.
        /// </summary>
        public static bool BlocksDelete(IForeignKey foreignKey)
        {
            if (foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull) return false;
            if (!foreignKey.DeclaringEntityType.HasSharedClrType && foreignKey.Properties.Count == 1
                && ClearedByDelete.Contains($"{foreignKey.DeclaringEntityType.ClrType.Name}.{foreignKey.Properties[0].Name}"))
            {
                return false;
            }
            return true;
        }

        /// <summary>A primary key pointing at the primary key of a base or derived type: TPT's table link.</summary>
        private static bool IsInheritanceLink(IForeignKey foreignKey)
        {
            var dependentKey = foreignKey.DeclaringEntityType.FindPrimaryKey();
            if (dependentKey == null || !foreignKey.PrincipalKey.IsPrimaryKey()) return false;
            if (!foreignKey.Properties.Select(p => p.Name).SequenceEqual(dependentKey.Properties.Select(p => p.Name))) return false;
            var dependentType = foreignKey.DeclaringEntityType.ClrType;
            var principalType = foreignKey.PrincipalEntityType.ClrType;
            return principalType.IsAssignableFrom(dependentType) || dependentType.IsAssignableFrom(principalType);
        }

        private static HashSet<string> ParseIgnore(IEnumerable<string>? ignore) =>
            new((ignore ?? Enumerable.Empty<string>())
                .SelectMany(entry => (entry ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(entry => entry.Split(':', 2))
                .Where(parts => parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                .Select(parts => IgnoreKey(parts[0], parts[1])),
                StringComparer.Ordinal);

        private static string IgnoreKey(string entityTypeName, string id) => $"{entityTypeName.ToLowerInvariant()}:{id}";

        /// <summary>
        /// (type name, property) whose value names the referencing row: for a join row the FK to the
        /// other side, for a plain FK the dependent's own key. (null, null) when it has no single key.
        /// </summary>
        private static (string? TypeName, IProperty? Property) ReferencingIdentity(IEntityType dependent, IForeignKey foreignKey)
        {
            if (dependent.HasSharedClrType)
            {
                var other = dependent.GetForeignKeys().FirstOrDefault(fk => fk != foreignKey && fk.Properties.Count == 1);
                return other == null ? (null, null) : (other.PrincipalEntityType.ClrType.Name, other.Properties[0]);
            }
            var key = dependent.FindPrimaryKey();
            return key is { Properties.Count: 1 } ? (dependent.ClrType.Name, key.Properties[0]) : (null, null);
        }

        private IEntityType? FindEntityType(string entityTypeName)
        {
            if (string.IsNullOrWhiteSpace(entityTypeName)) return null;
            var wanted = entityTypeName.Trim();
            return _context.Model.GetEntityTypes()
                .Where(t => !t.IsOwned() && !t.HasSharedClrType)
                .FirstOrDefault(t => string.Equals(t.ClrType.Name, wanted, StringComparison.OrdinalIgnoreCase));
        }

        private static object? ConvertKey(string id, Type keyType)
        {
            var type = Nullable.GetUnderlyingType(keyType) ?? keyType;
            if (type == typeof(int)) return int.TryParse(id, out var i) ? i : null;
            if (type == typeof(long)) return long.TryParse(id, out var l) ? l : null;
            if (type == typeof(Guid)) return Guid.TryParse(id, out var g) ? g : null;
            if (type == typeof(string)) return string.IsNullOrWhiteSpace(id) ? null : id;
            return null;
        }

        /// <summary>A join row reads as the entity on its other side ("Street (Districts)"), a plain FK as "GateStructure.DistrictId".</summary>
        private static string ReferenceLabel(IEntityType dependent, IForeignKey foreignKey)
        {
            if (dependent.HasSharedClrType)
            {
                var other = dependent.GetForeignKeys().FirstOrDefault(fk => fk != foreignKey);
                return other != null ? $"{other.PrincipalEntityType.ClrType.Name} (link)" : dependent.Name;
            }
            return $"{dependent.ClrType.Name}.{foreignKey.Properties[0].Name}";
        }

        private async Task<List<string?>> ReferencingRowsAsync(IEntityType dependent, IProperty foreignKey, object keyValue, IProperty? identity)
        {
            var queryable = dependent.HasSharedClrType
                ? SharedSet(dependent)
                : SetOf(dependent.ClrType);
            var identityProperty = identity ?? foreignKey;
            var method = ListMethod.MakeGenericMethod(dependent.ClrType, foreignKey.ClrType, identityProperty.ClrType);
            var task = (Task<List<string?>>)method.Invoke(null, new[] { queryable, foreignKey.Name, keyValue, identityProperty.Name })!;
            return await task;
        }

        private object SetOf(Type clrType)
        {
            var set = typeof(DbContext).GetMethods()
                .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                .MakeGenericMethod(clrType);
            return set.Invoke(_context, null)!;
        }

        private object SharedSet(IEntityType dependent)
        {
            var set = typeof(DbContext).GetMethods()
                .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
                .MakeGenericMethod(dependent.ClrType);
            return set.Invoke(_context, new object[] { dependent.Name })!;
        }

        private static async Task<List<string?>> ReferencingValuesAsync<TEntity, TProperty, TIdentity>(
            IQueryable<TEntity> source, string propertyName, object keyValue, string identityName)
            where TEntity : class
        {
            var row = Expression.Parameter(typeof(TEntity), "row");
            var propertyAccess = Expression.Call(
                typeof(EF), nameof(EF.Property), new[] { typeof(TProperty) }, row, Expression.Constant(propertyName));
            var underlying = Nullable.GetUnderlyingType(typeof(TProperty)) ?? typeof(TProperty);
            Expression value = Expression.Constant(Convert.ChangeType(keyValue, underlying), underlying);
            if (underlying != typeof(TProperty)) value = Expression.Convert(value, typeof(TProperty));
            var predicate = Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(propertyAccess, value), row);

            var selectRow = Expression.Parameter(typeof(TEntity), "row");
            var selector = Expression.Lambda<Func<TEntity, TIdentity>>(
                Expression.Call(typeof(EF), nameof(EF.Property), new[] { typeof(TIdentity) }, selectRow, Expression.Constant(identityName)),
                selectRow);

            var identities = await source.Where(predicate).Select(selector).ToListAsync();
            return identities.Select(identity => identity == null ? null : Convert.ToString(identity, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        }
    }
}
