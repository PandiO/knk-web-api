using System.Linq.Expressions;
using System.Reflection;
using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace knkwebapi_v2.Services.LocationRetention;

/// <summary>One foreign key that points at Location, as found in the EF Core model.</summary>
public sealed record LocationRelation(string EntityName, string? Table, string Column, bool IsRequired, bool IsJoinTable)
{
    public string Describe() => $"{EntityName}.{Column}";
}

/// <summary>
/// Which rows reference a Location, derived from the EF Core model metadata (KNG-80): every
/// foreign key whose principal is Location, declared on any entity type, including the shared-type
/// join entities of many-to-many skip navigations (gate_structure_guard_spawn_locations). Nothing
/// is listed by hand, so a new LocationId on any entity is covered the moment it is mapped;
/// LocationReferenceGraphTests fails if a new shape of FK is not.
/// <para>
/// The orphan predicate is one LINQ expression of NOT EXISTS anti-joins, so the query is a single
/// set-based statement per batch on MySQL and also runs on EF InMemory in tests.
/// </para>
/// </summary>
public static class LocationReferenceGraph
{
    /// <summary>The default name a Location gets on creation (Location.Name's initializer, the web
    /// app's location form default and the plugin's world-task capture). Null and empty also count
    /// as default (developer decision 2026-10-09).</summary>
    public const string DefaultName = "Location";

    private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethods()
        .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

    private static readonly MethodInfo SharedSetMethod = typeof(DbContext).GetMethods()
        .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

    /// <summary>Every FK to Location in the model, each declared once (not repeated on derived types).</summary>
    public static IReadOnlyList<IForeignKey> ForeignKeysTo(IModel model) =>
        model.GetEntityTypes()
            .SelectMany(t => t.GetDeclaredForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Location))
            .OrderBy(fk => fk.DeclaringEntityType.Name, StringComparer.Ordinal)
            .ThenBy(fk => fk.Properties[0].Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The relations as plain descriptions, for the run log, the admin panel and tests.</summary>
    public static IReadOnlyList<LocationRelation> Describe(IModel model) =>
        ForeignKeysTo(model).Select(fk =>
        {
            var dependent = fk.DeclaringEntityType;
            var property = fk.Properties.Single();
            var table = dependent.GetTableName();
            var column = table == null
                ? property.Name
                : property.GetColumnName(StoreObjectIdentifier.Table(table, dependent.GetSchema())) ?? property.Name;
            return new LocationRelation(dependent.ShortName(), table, column, fk.IsRequired, dependent.HasSharedClrType);
        }).ToList();

    /// <summary>l => the Location's name is still the default.</summary>
    public static Expression<Func<Location, bool>> HasDefaultName() =>
        l => l.Name == null || l.Name.Trim() == "" || l.Name == DefaultName;

    /// <summary>l => some row references the Location through any FK in the model.</summary>
    public static Expression<Func<Location, bool>> HasAnyRelation(DbContext context)
    {
        var location = Expression.Parameter(typeof(Location), "l");
        Expression? anyReference = null;

        foreach (var fk in ForeignKeysTo(context.Model))
        {
            var dependent = fk.DeclaringEntityType;
            if (dependent.IsOwned())
            {
                // An owned type has no set of its own to anti-join against; fail loudly instead of
                // silently treating its Locations as unreferenced.
                throw new NotSupportedException(
                    $"Location retention can't check the FK {dependent.ShortName()}.{fk.Properties[0].Name}: owned entity types are not supported.");
            }
            if (fk.Properties.Count != 1)
            {
                throw new NotSupportedException($"Location retention expects single-column FKs to Location, found {dependent.ShortName()}.");
            }

            var property = fk.Properties[0];
            var set = (IQueryable)(dependent.HasSharedClrType
                ? SharedSetMethod.MakeGenericMethod(dependent.ClrType).Invoke(context, new object[] { dependent.Name })!
                : SetMethod.MakeGenericMethod(dependent.ClrType).Invoke(context, null)!);

            // d => EF.Property<T>(d, "Fk") == (T)l.Id
            var row = Expression.Parameter(dependent.ClrType, "d");
            var fkValue = Expression.Call(typeof(EF), nameof(EF.Property), new[] { property.ClrType }, row, Expression.Constant(property.Name));
            var locationId = Expression.Convert(Expression.Property(location, nameof(Location.Id)), property.ClrType);
            var matches = Expression.Lambda(Expression.Equal(fkValue, locationId), row);
            var exists = Expression.Call(typeof(Queryable), nameof(Queryable.Any), new[] { dependent.ClrType }, set.Expression, matches);

            anyReference = anyReference == null ? exists : Expression.OrElse(anyReference, exists);
        }

        return Expression.Lambda<Func<Location, bool>>(anyReference ?? Expression.Constant(false), location);
    }

    /// <summary>
    /// l => default name, no FK reference, and (when <paramref name="createdBefore"/> is given)
    /// created before it. A null CreatedAt (rows older than the column) counts as old.
    /// </summary>
    public static Expression<Func<Location, bool>> IsOrphan(DbContext context, DateTime? createdBefore)
    {
        var defaultName = HasDefaultName();
        var related = HasAnyRelation(context);
        var location = defaultName.Parameters[0];
        Expression body = Expression.AndAlso(
            defaultName.Body,
            Expression.Not(new ParameterReplacer(related.Parameters[0], location).Visit(related.Body)));

        if (createdBefore.HasValue)
        {
            Expression<Func<Location, bool>> old = l => l.CreatedAt == null || l.CreatedAt < createdBefore.Value;
            body = Expression.AndAlso(body, new ParameterReplacer(old.Parameters[0], location).Visit(old.Body));
        }

        return Expression.Lambda<Func<Location, bool>>(body, location);
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterReplacer(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : node;
    }
}
