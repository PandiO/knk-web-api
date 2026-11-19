using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services
{
    // Siege weapons phase 2 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 3, § 4.3; W3, W70;
    // PandiO/knk-workspace#783): a spot's size class from its bounds and facing. The footprint is read as width across
    // the facing x length along it x height; the class is the largest one that fits, and bounds smaller than the small
    // class have none (refused, A03). The same numbers and rule are in knk-plugin's knk-core
    // (core/siege/weapons/SpotSizeClass, SpotSizing), which the in-game spot task uses; keep both in step.
    public static class SiegeWeaponSpotSizing
    {
        // Width across the facing, length along it, height (§ 3 size class table).
        private static readonly (SiegeWeaponSizeClass Class, int Width, int Length, int Height)[] Classes =
        {
            (SiegeWeaponSizeClass.Small, 7, 9, 5),
            (SiegeWeaponSizeClass.Medium, 7, 9, 8),
            (SiegeWeaponSizeClass.Large, 9, 15, 16),
        };

        // Bounds are inclusive block corners (min <= max on every axis).
        public static (int Width, int Length, int Height) Footprint(int minX, int minY, int minZ, int maxX, int maxY,
            int maxZ, SiegeWeaponFacing facing)
        {
            var sizeX = maxX - minX + 1;
            var sizeY = maxY - minY + 1;
            var sizeZ = maxZ - minZ + 1;
            var alongZ = facing is SiegeWeaponFacing.North or SiegeWeaponFacing.South;
            return alongZ ? (sizeX, sizeZ, sizeY) : (sizeZ, sizeX, sizeY);
        }

        // The largest class that fits, or null when even the small class doesn't.
        public static SiegeWeaponSizeClass? ClassFor(int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
            SiegeWeaponFacing facing)
        {
            var (width, length, height) = Footprint(minX, minY, minZ, maxX, maxY, maxZ, facing);
            SiegeWeaponSizeClass? best = null;
            foreach (var c in Classes)
            {
                if (width >= c.Width && length >= c.Length && height >= c.Height) best = c.Class;
            }
            return best;
        }

        public static string Describe(SiegeWeaponSizeClass sizeClass)
        {
            var c = Classes.First(x => x.Class == sizeClass);
            return $"{c.Width} wide x {c.Length} long x {c.Height} high";
        }
    }
}
