using System.Runtime.CompilerServices;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace knkwebapi_v2.Services
{
    /// <summary>How a relationship is stored (decides what Select/Add and Unlink change).</summary>
    public enum RelationshipStorage
    {
        /// <summary>A column on this entity (<c>Category.ParentCategoryId</c>): Select sets it, Unlink clears it.</summary>
        ForeignKey,
        /// <summary>A column on the related row (<c>Category.ChildCategories</c> → <c>ParentCategoryId</c>): Add sets it to this id.</summary>
        Children,
        /// <summary>A hidden link table (<c>Town.Streets</c> → <c>TownStreet</c>): Add/Unlink insert or remove the link row.</summary>
        Many,
        /// <summary>A link entity with only the two ids (<c>ItemBlueprint.Tags</c> → <c>ItemBlueprintTag</c>).</summary>
        LinkRow,
        /// <summary>Anything else (this entity is the principal of a one-to-one): only through the Edit page.</summary>
        Other,
    }

    /// <summary>One relationship of a dashboard type: its storage, what may change and the EF metadata to change it with.</summary>
    public sealed class RelationshipDescriptor
    {
        internal RelationshipDescriptor() { }

        public string OwnerType { get; internal init; } = null!;
        public Type OwnerClr { get; internal init; } = null!;
        /// <summary>The CLR navigation name (<c>ParentCategory</c>).</summary>
        public string Property { get; internal init; } = null!;
        /// <summary>The related type the page links to (for a link row: the other side).</summary>
        public string RelatedType { get; internal init; } = null!;
        public Type RelatedClr { get; internal init; } = null!;
        public bool IsCollection { get; internal init; }
        public RelationshipStorage Storage { get; internal init; }
        public bool CanLink { get; internal init; }
        public bool CanUnlink { get; internal init; }
        public string? LinkReason { get; internal init; }
        public string? UnlinkReason { get; internal init; }
        /// <summary>All of these are needed for a change.</summary>
        public IReadOnlyList<string> Nodes { get; internal init; } = Array.Empty<string>();
        /// <summary>The column that changes: on the owner (ForeignKey), on the related row (Children), the owner's id on a link row.</summary>
        public string? ForeignKeyName { get; internal init; }
        public bool ForeignKeyNullable { get; internal init; }
        /// <summary>Link rows: the column with the other side's id and the navigation to it.</summary>
        public string? OtherForeignKeyName { get; internal init; }
        public string? OtherNavigationName { get; internal init; }
        public Type? LinkRowClr { get; internal init; }
        /// <summary>A parent/child relationship of a type with itself (categories): Select and Add refuse cycles.</summary>
        public bool SelfReferencing { get; internal init; }

        public RelationshipMetadataDto ToDto() => new()
        {
            Property = Camel(Property),
            RelatedType = RelatedType,
            Kind = IsCollection ? "collection" : "reference",
            Storage = Storage switch
            {
                RelationshipStorage.ForeignKey => "foreignKey",
                RelationshipStorage.Children => "children",
                RelationshipStorage.Many => "many",
                RelationshipStorage.LinkRow => "linkRow",
                _ => "other",
            },
            CanLink = CanLink,
            CanUnlink = CanUnlink,
            LinkReason = CanLink ? null : LinkReason,
            UnlinkReason = CanUnlink ? null : UnlinkReason,
            Nodes = Nodes.ToList(),
            RelatedIdField = OtherForeignKeyName == null ? null : Camel(OtherForeignKeyName),
            RelatedNavigation = OtherNavigationName == null ? null : Camel(OtherNavigationName),
        };

        internal static string Camel(string name) => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// The relationships of every dashboard type (PandiO/knk-workspace#188), read from the EF model once per model.
    /// <para>
    /// Only the pairs in <see cref="Supported"/> change through <c>api/Relationships</c>: their services do nothing on such
    /// a change beyond checking that the related row exists (reviewed 2026-11-09). The rest answer "change it on the Edit
    /// page" with a reason: required parents (a district's town, a structure's street: world, region and siege rules run
    /// in the type's own update), locations and teleport settings of domains (the world is resolved from them), gates, kits
    /// (slot rules), lootbox and siege setup (the game server's caches and match rules) and the players area (the
    /// permission escalation guard). Adding a pair is one line here after a look at the type's update service.
    /// </para>
    /// </summary>
    public static class RelationshipCatalog
    {
        /// <summary>Type.Property → (Select/Add, Unlink). Icons: select another one, never none (KNG-200 D8).</summary>
        public static readonly IReadOnlyDictionary<string, (bool Link, bool Unlink)> Supported =
            new Dictionary<string, (bool, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Category.ParentCategory"] = (true, true),
                ["Category.ChildCategories"] = (true, true),
                ["Category.Tags"] = (true, true),
                ["Category.IconMaterialRef"] = (true, false),
                ["ItemBlueprint.Category"] = (true, true),
                ["ItemBlueprint.Grade"] = (true, true),
                ["ItemBlueprint.Tags"] = (true, true),
                ["ItemBlueprint.IconMaterial"] = (true, false),
                ["Town.Streets"] = (true, true),
                ["District.Streets"] = (true, true),
                ["Street.Districts"] = (true, true),
                ["Street.Town"] = (true, true),
            };

        public const string EditPageReason = "Change this on the Edit page.";
        public const string IconRequiredReason = "An icon is required: select another one.";

        private static readonly ConditionalWeakTable<IModel, IReadOnlyDictionary<string, IReadOnlyList<RelationshipDescriptor>>> Cache = new();

        /// <summary>The relationships of a dashboard type (empty for an unknown type or one without navigations).</summary>
        public static IReadOnlyList<RelationshipDescriptor> For(IModel model, string typeName)
        {
            var all = Cache.GetValue(model, Build);
            var entry = DashboardAccess.Find(typeName);
            return entry != null && all.TryGetValue(entry.TypeName, out var list) ? list : Array.Empty<RelationshipDescriptor>();
        }

        /// <summary>One relationship by navigation name (any case), or null.</summary>
        public static RelationshipDescriptor? Find(IModel model, string typeName, string property) =>
            For(model, typeName).FirstOrDefault(r => string.Equals(r.Property, property?.Trim(), StringComparison.OrdinalIgnoreCase));

        private static IReadOnlyDictionary<string, IReadOnlyList<RelationshipDescriptor>> Build(IModel model)
        {
            var result = new Dictionary<string, IReadOnlyList<RelationshipDescriptor>>(StringComparer.Ordinal);
            foreach (var type in DashboardAccess.All)
            {
                var entity = model.GetEntityTypes().FirstOrDefault(e => e.ClrType.Name == type.TypeName && !e.IsOwned());
                if (entity?.FindPrimaryKey() is not { Properties.Count: 1 }) continue;
                var list = new List<RelationshipDescriptor>();
                foreach (var navigation in entity.GetNavigations()) list.Add(Describe(type, entity, navigation));
                foreach (var skip in entity.GetSkipNavigations()) list.Add(DescribeMany(type, entity, skip));
                result[type.TypeName] = list;
            }
            return result;
        }

        private static RelationshipDescriptor Describe(DashboardTypeAccess owner, IEntityType entity, INavigation navigation)
        {
            var target = navigation.TargetEntityType;
            var fk = navigation.ForeignKey;
            var fkProperty = fk.Properties.Count == 1 ? fk.Properties[0] : null;
            var key = $"{owner.TypeName}.{navigation.Name}";
            var supported = Supported.TryGetValue(key, out var allowed) && owner.Area != DashboardArea.Players;
            var selfReferencing = target.ClrType == entity.ClrType;
            var ownerLabel = owner.TypeName;

            if (!navigation.IsCollection && navigation.IsOnDependent && fkProperty != null)
            {
                var nullable = fkProperty.IsNullable;
                return new RelationshipDescriptor
                {
                    OwnerType = owner.TypeName, OwnerClr = entity.ClrType, Property = navigation.Name,
                    RelatedType = target.ClrType.Name, RelatedClr = target.ClrType, IsCollection = false,
                    Storage = RelationshipStorage.ForeignKey, ForeignKeyName = fkProperty.Name, ForeignKeyNullable = nullable,
                    SelfReferencing = selfReferencing,
                    CanLink = supported && allowed.Link,
                    CanUnlink = supported && allowed.Unlink && nullable,
                    LinkReason = EditPageReason,
                    UnlinkReason = !nullable
                        ? $"A {ownerLabel} always has a {target.ClrType.Name}: change it on the Edit page."
                        : supported ? IconRequiredReason : EditPageReason,
                    Nodes = new[] { owner.WriteNode },
                };
            }

            if (navigation.IsCollection && fkProperty != null)
            {
                // A link entity with only the two ids, keyed by them: the page shows the other side.
                var other = LinkRowOther(target, fk);
                if (other != null)
                {
                    return new RelationshipDescriptor
                    {
                        OwnerType = owner.TypeName, OwnerClr = entity.ClrType, Property = navigation.Name,
                        RelatedType = other.PrincipalEntityType.ClrType.Name, RelatedClr = other.PrincipalEntityType.ClrType,
                        IsCollection = true, Storage = RelationshipStorage.LinkRow, ForeignKeyName = fkProperty.Name,
                        OtherForeignKeyName = other.Properties[0].Name, OtherNavigationName = other.DependentToPrincipal?.Name,
                        LinkRowClr = target.ClrType,
                        CanLink = supported && allowed.Link, CanUnlink = supported && allowed.Unlink,
                        LinkReason = EditPageReason, UnlinkReason = EditPageReason,
                        Nodes = new[] { owner.WriteNode },
                    };
                }

                var nullable = fkProperty.IsNullable;
                var related = DashboardAccess.Find(target.ClrType.Name);
                var relatedLabel = target.ClrType.Name;
                var nodes = new List<string> { owner.WriteNode };
                if (related != null && !nodes.Contains(related.WriteNode)) nodes.Add(related.WriteNode);
                var childrenSupported = supported && nullable && related != null && related.Area != DashboardArea.Players;
                return new RelationshipDescriptor
                {
                    OwnerType = owner.TypeName, OwnerClr = entity.ClrType, Property = navigation.Name,
                    RelatedType = relatedLabel, RelatedClr = target.ClrType, IsCollection = true,
                    Storage = RelationshipStorage.Children, ForeignKeyName = fkProperty.Name, ForeignKeyNullable = nullable,
                    SelfReferencing = selfReferencing,
                    CanLink = childrenSupported && allowed.Link,
                    CanUnlink = childrenSupported && allowed.Unlink,
                    LinkReason = nullable
                        ? EditPageReason
                        : $"A {relatedLabel} always belongs to one {ownerLabel}: create a new one here, or change it on the {relatedLabel}'s Edit page.",
                    UnlinkReason = nullable
                        ? EditPageReason
                        : $"A {relatedLabel} always belongs to one {ownerLabel}: delete it, or move it on its own Edit page.",
                    Nodes = nodes,
                };
            }

            return new RelationshipDescriptor
            {
                OwnerType = owner.TypeName, OwnerClr = entity.ClrType, Property = navigation.Name,
                RelatedType = target.ClrType.Name, RelatedClr = target.ClrType, IsCollection = navigation.IsCollection,
                Storage = RelationshipStorage.Other, LinkReason = EditPageReason, UnlinkReason = EditPageReason,
                Nodes = new[] { owner.WriteNode },
            };
        }

        private static RelationshipDescriptor DescribeMany(DashboardTypeAccess owner, IEntityType entity, ISkipNavigation skip)
        {
            var supported = Supported.TryGetValue($"{owner.TypeName}.{skip.Name}", out var allowed) && owner.Area != DashboardArea.Players;
            return new RelationshipDescriptor
            {
                OwnerType = owner.TypeName, OwnerClr = entity.ClrType, Property = skip.Name,
                RelatedType = skip.TargetEntityType.ClrType.Name, RelatedClr = skip.TargetEntityType.ClrType, IsCollection = true,
                Storage = RelationshipStorage.Many,
                CanLink = supported && allowed.Link, CanUnlink = supported && allowed.Unlink,
                LinkReason = EditPageReason, UnlinkReason = EditPageReason,
                Nodes = new[] { owner.WriteNode },
            };
        }

        /// <summary>The other foreign key of a pure link entity (key = the two ids, nothing else stored), or null.</summary>
        private static IForeignKey? LinkRowOther(IEntityType row, IForeignKey toOwner)
        {
            var keys = row.FindPrimaryKey()?.Properties;
            var fks = row.GetForeignKeys().ToList();
            if (keys is not { Count: 2 } || fks.Count != 2) return null;
            var other = fks.FirstOrDefault(f => f != toOwner);
            if (other is not { Properties.Count: 1 } || toOwner.Properties.Count != 1) return null;
            if (!keys.Contains(toOwner.Properties[0]) || !keys.Contains(other.Properties[0])) return null;
            // Anything stored besides the two ids (a level, a weight) makes it a record of its own: Create New / Edit.
            return row.GetProperties().All(p => keys.Contains(p)) ? other : null;
        }
    }

    /// <summary>A refused relationship change: the HTTP status, a code for the web app and a sentence for the admin.</summary>
    public sealed class RelationshipException : Exception
    {
        public RelationshipException(int status, string code, string message, int? currentId = null) : base(message)
        {
            Status = status;
            Code = code;
            CurrentId = currentId;
        }

        public int Status { get; }
        public string Code { get; }
        /// <summary>409 AlreadyLinked: the parent the child has now.</summary>
        public int? CurrentId { get; }
    }

    /// <summary>
    /// Select / Unlink / Add on the entity detail page (PandiO/knk-workspace#188). Changes only the link: the related
    /// entity is never deleted (a link row or a hidden link-table row is the link itself). The caller
    /// (<see cref="RequireRelationshipWriteAttribute"/>) has checked the nodes.
    /// </summary>
    public interface IRelationshipService
    {
        IReadOnlyList<RelationshipDescriptor> Describe(string typeName);

        RelationshipDescriptor? Find(string typeName, string property);

        Task<RelationshipChangeResultDto> LinkAsync(string typeName, int id, string property, int relatedId, bool move);

        Task<RelationshipChangeResultDto> UnlinkAsync(string typeName, int id, string property, int? relatedId);
    }

    public class RelationshipService : IRelationshipService
    {
        /// <summary>How far a parent chain is followed when checking for a cycle.</summary>
        public const int MaxChainDepth = 1000;

        private readonly KnKDbContext _db;
        private readonly ILogger<RelationshipService>? _logger;

        public RelationshipService(KnKDbContext db, ILogger<RelationshipService>? logger = null)
        {
            _db = db;
            _logger = logger;
        }

        public IReadOnlyList<RelationshipDescriptor> Describe(string typeName) => RelationshipCatalog.For(_db.Model, typeName);

        public RelationshipDescriptor? Find(string typeName, string property) => RelationshipCatalog.Find(_db.Model, typeName, property);

        public async Task<RelationshipChangeResultDto> LinkAsync(string typeName, int id, string property, int relatedId, bool move)
        {
            var relationship = Require(typeName, property);
            if (!relationship.CanLink) throw new RelationshipException(400, "RelationshipNotEditable", relationship.LinkReason ?? RelationshipCatalog.EditPageReason);
            if (relatedId <= 0) throw new RelationshipException(400, "InvalidRelatedId", "Pick the record to link.");
            var owner = await OwnerAsync(relationship, id);
            var result = Result(relationship, id, relatedId);

            switch (relationship.Storage)
            {
                case RelationshipStorage.ForeignKey:
                {
                    await RelatedAsync(relationship, relatedId);
                    var column = _db.Entry(owner).Property(relationship.ForeignKeyName!);
                    var previous = AsId(column.CurrentValue);
                    if (previous == relatedId) return result;
                    if (relationship.SelfReferencing) await RefuseCycleAsync(relationship, start: relatedId, forbidden: id);
                    column.CurrentValue = relatedId;
                    result.PreviousId = previous;
                    break;
                }
                case RelationshipStorage.Children:
                {
                    var related = await RelatedAsync(relationship, relatedId);
                    var column = _db.Entry(related).Property(relationship.ForeignKeyName!);
                    var previous = AsId(column.CurrentValue);
                    if (previous == id) return result;
                    if (relationship.SelfReferencing)
                    {
                        if (relatedId == id) throw Cycle(relationship);
                        await RefuseCycleAsync(relationship, start: id, forbidden: relatedId);
                    }
                    if (previous != null && !move)
                    {
                        throw new RelationshipException(409, "AlreadyLinked",
                            $"This {relationship.RelatedType} already belongs to {relationship.OwnerType} #{previous}. Move it here?", previous);
                    }
                    column.CurrentValue = id;
                    result.PreviousId = previous;
                    break;
                }
                case RelationshipStorage.Many:
                {
                    var related = await RelatedAsync(relationship, relatedId);
                    var collection = _db.Entry(owner).Collection(relationship.Property);
                    await collection.LoadAsync();
                    if (Contains(collection.CurrentValue, related)) return result;
                    Add(collection.CurrentValue, related);
                    break;
                }
                case RelationshipStorage.LinkRow:
                {
                    await RelatedAsync(relationship, relatedId);
                    if (await LinkRowAsync(relationship, id, relatedId) != null) return result;
                    var row = Activator.CreateInstance(relationship.LinkRowClr!)!;
                    var entry = _db.Entry(row);
                    entry.Property(relationship.ForeignKeyName!).CurrentValue = id;
                    entry.Property(relationship.OtherForeignKeyName!).CurrentValue = relatedId;
                    _db.Add(row);
                    break;
                }
                default:
                    throw new RelationshipException(400, "RelationshipNotEditable", RelationshipCatalog.EditPageReason);
            }

            await SaveAsync(relationship);
            result.Changed = true;
            return result;
        }

        public async Task<RelationshipChangeResultDto> UnlinkAsync(string typeName, int id, string property, int? relatedId)
        {
            var relationship = Require(typeName, property);
            if (!relationship.CanUnlink) throw new RelationshipException(400, "RelationshipNotEditable", relationship.UnlinkReason ?? RelationshipCatalog.EditPageReason);
            if (relationship.IsCollection && relatedId is not > 0) throw new RelationshipException(400, "InvalidRelatedId", "Name the record to unlink.");
            var owner = await OwnerAsync(relationship, id);
            var result = Result(relationship, id, relatedId);

            switch (relationship.Storage)
            {
                case RelationshipStorage.ForeignKey:
                {
                    var column = _db.Entry(owner).Property(relationship.ForeignKeyName!);
                    var previous = AsId(column.CurrentValue);
                    if (previous == null) return result;
                    if (relatedId is > 0 && relatedId != previous) throw NotLinked(relationship);
                    column.CurrentValue = null;
                    result.PreviousId = previous;
                    result.RelatedId = previous;
                    break;
                }
                case RelationshipStorage.Children:
                {
                    var related = await RelatedAsync(relationship, relatedId!.Value);
                    var column = _db.Entry(related).Property(relationship.ForeignKeyName!);
                    if (AsId(column.CurrentValue) != id) throw NotLinked(relationship);
                    column.CurrentValue = null;
                    break;
                }
                case RelationshipStorage.Many:
                {
                    var related = await RelatedAsync(relationship, relatedId!.Value);
                    var collection = _db.Entry(owner).Collection(relationship.Property);
                    await collection.LoadAsync();
                    if (!Contains(collection.CurrentValue, related)) throw NotLinked(relationship);
                    Remove(collection.CurrentValue, related);
                    break;
                }
                case RelationshipStorage.LinkRow:
                {
                    var row = await LinkRowAsync(relationship, id, relatedId!.Value) ?? throw NotLinked(relationship);
                    // The link row is the link itself; the tag (or other side) stays.
                    _db.Remove(row);
                    break;
                }
                default:
                    throw new RelationshipException(400, "RelationshipNotEditable", RelationshipCatalog.EditPageReason);
            }

            await SaveAsync(relationship);
            result.Changed = true;
            return result;
        }

        private RelationshipDescriptor Require(string typeName, string property)
        {
            if (DashboardAccess.Find(typeName) == null) throw new RelationshipException(404, "UnknownEntityType", "Not a dashboard entity type.");
            return Find(typeName, property)
                ?? throw new RelationshipException(404, "UnknownRelationship", $"{DashboardAccess.Find(typeName)!.TypeName} has no relationship '{property}'.");
        }

        private static RelationshipChangeResultDto Result(RelationshipDescriptor relationship, int id, int? relatedId) => new()
        {
            EntityType = relationship.OwnerType,
            Id = id,
            Property = RelationshipDescriptor.Camel(relationship.Property),
            RelatedId = relatedId,
        };

        private async Task<object> OwnerAsync(RelationshipDescriptor relationship, int id) =>
            await _db.FindAsync(relationship.OwnerClr, id)
            ?? throw new RelationshipException(404, "NotFound", $"{relationship.OwnerType} {id} not found.");

        private async Task<object> RelatedAsync(RelationshipDescriptor relationship, int relatedId) =>
            await _db.FindAsync(relationship.RelatedClr, relatedId)
            ?? throw new RelationshipException(404, "RelatedNotFound", $"{relationship.RelatedType} {relatedId} not found (deleted?).");

        private async Task<object?> LinkRowAsync(RelationshipDescriptor relationship, int id, int relatedId)
        {
            var key = _db.Model.FindEntityType(relationship.LinkRowClr!)!.FindPrimaryKey()!.Properties;
            var values = key.Select(p => (object)(p.Name == relationship.ForeignKeyName ? id : relatedId)).ToArray();
            return await _db.FindAsync(relationship.LinkRowClr!, values);
        }

        /// <summary>Follows the parent column from <paramref name="start"/> upwards; meeting <paramref name="forbidden"/> is a cycle.</summary>
        private async Task RefuseCycleAsync(RelationshipDescriptor relationship, int start, int forbidden)
        {
            // Both self-references use the same column: ParentCategoryId (reference) and ChildCategories (children).
            var column = relationship.ForeignKeyName!;
            int? current = start;
            for (var depth = 0; current != null && depth < MaxChainDepth; depth++)
            {
                if (current == forbidden) throw Cycle(relationship);
                var row = await _db.FindAsync(relationship.OwnerClr, current.Value);
                if (row == null) return;
                current = AsId(_db.Entry(row).Property(column).CurrentValue);
            }
        }

        private async Task SaveAsync(RelationshipDescriptor relationship)
        {
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger?.LogWarning("Relationship change {Type}.{Property} refused by the database: {Error}", relationship.OwnerType,
                    relationship.Property, ex.InnerException?.GetType().Name ?? ex.GetType().Name);
                throw new RelationshipException(409, "Conflict", "The change couldn't be saved: it clashes with another record. Reload and try again.");
            }
        }

        private static RelationshipException Cycle(RelationshipDescriptor relationship) =>
            new(400, "Cycle", $"A {relationship.OwnerType} can't be inside itself: pick one that isn't below this one.");

        private static RelationshipException NotLinked(RelationshipDescriptor relationship) =>
            new(404, "NotLinked", $"That {relationship.RelatedType} isn't linked here (any more). Reload to see the current links.");

        private static int? AsId(object? value) => value == null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);

        // The collections are ICollection<T>; tracked entities are resolved to one instance per id, so reference equality holds.
        private static bool Contains(object? collection, object item) =>
            collection is System.Collections.IEnumerable items && items.Cast<object>().Any(i => ReferenceEquals(i, item));

        private static void Add(object? collection, object item) => Invoke(collection, "Add", item);

        private static void Remove(object? collection, object item) => Invoke(collection, "Remove", item);

        private static void Invoke(object? collection, string method, object item)
        {
            if (collection == null) throw new InvalidOperationException("The collection isn't loaded.");
            var target = collection.GetType().GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));
            target.GetMethod(method)!.Invoke(collection, new[] { item });
        }
    }
}
