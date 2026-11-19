namespace knkwebapi_v2.Enums
{
    // Production engine P3 (PandiO/knk-workspace#86, PRODUCTION_KINDS.md § 2-5). Stored as strings, so new members are
    // additive and the order is free to change.

    /// <summary>Where a production kind's resources come from (PRODUCTION_KINDS § 3.1). Code strategies used by P4.</summary>
    public enum ProductionAreaModel
    {
        BorderedField,
        ExclusiveVolume,
        SharedRadius,
        Pasture,
        HuntingGround,
    }

    /// <summary>What weather and later modifiers target (PRODUCTION_KINDS § 2). A line can override its kind's.</summary>
    public enum ResourceCategory
    {
        Crops,
        Wood,
        Stone,
        Ore,
        Fish,
        Flowers,
        Animals,
    }

    /// <summary>Where a line's base rate comes from (PRODUCTION_KINDS § 4.1). P3 builds Configured; P4 adds the others
    /// as further <c>IProductionBaseRateProvider</c>s (P-D2).</summary>
    public enum ProductionRateSource
    {
        Configured,
        Derived,
        Hybrid,
    }

    /// <summary>A production line's state (#86).</summary>
    public enum ProductionLineStatus
    {
        Running,
        /// <summary>Stopped by staff (the line's own switch).</summary>
        Paused,
        /// <summary>The output storage refused the last emit; no accrual until it has room again (D4).</summary>
        BlockedFull,
        /// <summary>The input storage lacked the inputs for the last cycle; no accrual until they are there.</summary>
        BlockedInputs,
        /// <summary>The line can't run as configured (no output storage, missing blueprint); see the status reason.</summary>
        Error,
    }

    /// <summary>Where a production modifier applies (PRODUCTION_KINDS § 4.2).</summary>
    public enum ProductionModifierScope
    {
        Global,
        Kingdom,
        Town,
        /// <summary>The rural structures of a town (its territory outside its districts).</summary>
        Territory,
        /// <summary>A map grid cell (the painted map, Later): stored, never matched in V1.</summary>
        MapArea,
        Structure,
        /// <summary>One production line.</summary>
        Output,
    }

    /// <summary>What a production modifier targets (PRODUCTION_KINDS § 4.2).</summary>
    public enum ProductionModifierTarget
    {
        /// <summary>Every output in scope.</summary>
        Any,
        Blueprint,
        Category,
        Kind,
    }

    /// <summary>How a modifier changes the rate. Additions to the base first, then multiplications (F8).</summary>
    public enum ProductionModifierEffect
    {
        Add,
        Multiply,
    }

    /// <summary>The level items of a production kind level (PRODUCTION_KINDS § 5).</summary>
    public enum ProductionKindLevelItemRole
    {
        /// <summary>The blueprint is produced only from this level on (<c>OutputUnlocks</c>).</summary>
        Unlock,
        /// <summary>Part of the cost of upgrading to this level (blueprint × amount, <c>UpgradeCost</c>).</summary>
        UpgradeCost,
    }
}

namespace knkwebapi_v2.Enums
{
    // Yield areas and surveys P4 (PandiO/knk-workspace#138, PRODUCTION_KINDS.md § 3-4). Stored as strings.

    /// <summary>How a production area's shape is stored (PRODUCTION_KINDS § 3.1).</summary>
    public enum ProductionAreaShape
    {
        /// <summary>A WorldGuard region (a managed region parented to the structure); its bounding box is reported with it.</summary>
        Region,
        /// <summary>A WorldEdit cuboid: two corners.</summary>
        Cuboid,
        /// <summary>A WorldEdit polygon: x/z points plus a y range.</summary>
        Polygon,
        /// <summary>A radius around a centre (default: the structure's Location).</summary>
        Radius,
    }

    /// <summary>Why a survey ran (A10, G5).</summary>
    public enum AreaSurveyTrigger
    {
        Creation,
        /// <summary>On demand: the command or the web app button.</summary>
        Admin,
        /// <summary>The daily survey.</summary>
        Scheduled,
        /// <summary>After a notable change (e.g. many harvested blocks).</summary>
        NotableChange,
    }

    /// <summary>A Hybrid override of one output (PRODUCTION_KINDS § 4.1).</summary>
    public enum ProductionYieldOverrideKind
    {
        /// <summary>Always produce the output at a fixed base rate per hour, whatever the survey says.</summary>
        Pin,
        /// <summary>Never produce the output.</summary>
        Exclude,
        /// <summary>Multiply the survey-derived base rate.</summary>
        Scale,
    }
}
