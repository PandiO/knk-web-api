using System.Linq.Expressions;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// Every foreign key to <see cref="MinecraftMaterialRef"/>, so the audit can count references and a merge
/// can repoint them (KNG-200 §8). <c>MinecraftMaterialReferencesTests</c> pins this list against the EF
/// model: a new FK to the material table fails that test until it is added here.
/// </summary>
public static class MinecraftMaterialReferences
{
    public sealed class Referrer
    {
        internal Referrer(
            string name,
            Type entityType,
            string foreignKey,
            Func<KnKDbContext, CancellationToken, Task<List<int>>> loadIds,
            Func<KnKDbContext, int, CancellationToken, Task<List<object>>> loadReferencing,
            Action<object, int> setForeignKey)
        {
            Name = name;
            EntityType = entityType;
            ForeignKey = foreignKey;
            _loadIds = loadIds;
            _loadReferencing = loadReferencing;
            _setForeignKey = setForeignKey;
        }

        private readonly Func<KnKDbContext, CancellationToken, Task<List<int>>> _loadIds;
        private readonly Func<KnKDbContext, int, CancellationToken, Task<List<object>>> _loadReferencing;
        private readonly Action<object, int> _setForeignKey;

        /// <summary>Label used in audit reports, e.g. <c>ItemBlueprint.IconMaterialRefId</c>.</summary>
        public string Name { get; }

        public Type EntityType { get; }

        public string ForeignKey { get; }

        /// <summary>The material id of every row that references a material (one entry per row).</summary>
        public Task<List<int>> LoadReferencedIdsAsync(KnKDbContext db, CancellationToken ct) => _loadIds(db, ct);

        /// <summary>Points every row that references <paramref name="sourceId"/> at <paramref name="targetId"/> (tracked; caller saves).</summary>
        public async Task<List<object>> RepointAsync(KnKDbContext db, int sourceId, int targetId, CancellationToken ct)
        {
            var rows = await _loadReferencing(db, sourceId, ct);
            foreach (var row in rows) _setForeignKey(row, targetId);
            return rows;
        }
    }

    public static readonly IReadOnlyList<Referrer> All = new[]
    {
        Make<Category>(nameof(Category.IconMaterialRefId), x => x.IconMaterialRefId, (x, id) => x.IconMaterialRefId = id),
        Make<ItemBlueprint>(nameof(ItemBlueprint.IconMaterialRefId), x => x.IconMaterialRefId, (x, id) => x.IconMaterialRefId = id),
        Make<GateStructure>(nameof(GateStructure.IconMaterialRefId), x => x.IconMaterialRefId, (x, id) => x.IconMaterialRefId = id),
        Make<GateDoor>(nameof(GateDoor.FallbackMaterialRefId), x => x.FallbackMaterialRefId, (x, id) => x.FallbackMaterialRefId = id),
        Make<MenuTemplate>(nameof(MenuTemplate.BackgroundMaterialRefId), x => x.BackgroundMaterialRefId, (x, id) => x.BackgroundMaterialRefId = id),
        Make<MenuItemTemplate>(nameof(MenuItemTemplate.MaterialRefId), x => x.MaterialRefId, (x, id) => x.MaterialRefId = id),
        Make<LootboxType>(nameof(LootboxType.DisplayMaterialRefId), x => x.DisplayMaterialRefId, (x, id) => x.DisplayMaterialRefId = id),
        Make<MinecraftBlockRef>(nameof(MinecraftBlockRef.MaterialRefId), x => x.MaterialRefId, (x, id) => x.MaterialRefId = id),
    };

    /// <summary>Reference counts per material id and referrer name, for the given ids (all when null).</summary>
    public static async Task<Dictionary<int, Dictionary<string, int>>> CountAsync(
        KnKDbContext db, IReadOnlyCollection<int>? materialIds, CancellationToken ct)
    {
        var wanted = materialIds?.ToHashSet();
        var result = new Dictionary<int, Dictionary<string, int>>();
        foreach (var referrer in All)
        {
            foreach (var id in await referrer.LoadReferencedIdsAsync(db, ct))
            {
                if (wanted != null && !wanted.Contains(id)) continue;
                if (!result.TryGetValue(id, out var perReferrer)) result[id] = perReferrer = new Dictionary<string, int>();
                perReferrer[referrer.Name] = perReferrer.GetValueOrDefault(referrer.Name) + 1;
            }
        }
        return result;
    }

    private static Referrer Make<T>(string foreignKey, Expression<Func<T, int?>> fk, Action<T, int> set) where T : class
    {
        var equalsParameter = fk.Parameters[0];
        return new Referrer(
            $"{typeof(T).Name}.{foreignKey}",
            typeof(T),
            foreignKey,
            async (db, ct) =>
            {
                var ids = await db.Set<T>().Select(fk).Where(id => id != null).ToListAsync(ct);
                return ids.Select(id => id!.Value).ToList();
            },
            async (db, sourceId, ct) =>
            {
                var equals = Expression.Lambda<Func<T, bool>>(
                    Expression.Equal(fk.Body, Expression.Constant((int?)sourceId, typeof(int?))), equalsParameter);
                var rows = await db.Set<T>().Where(equals).ToListAsync(ct);
                return rows.Cast<object>().ToList();
            },
            (row, id) => set((T)row, id));
    }
}
