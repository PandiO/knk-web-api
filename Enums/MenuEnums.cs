namespace knkwebapi_v2.Enums
{
    /// <summary>FR-2.1.1: Menu growth behavior.</summary>
    public enum MenuGrowthMode
    {
        Static,
        Dynamic
    }

    /// <summary>
    /// Preset section type, per IMPLEMENTATION_PLAN.md's "kind field selects a preset
    /// section type" — drives which renderer/field-schema a MenuSectionTemplate uses
    /// (Phase 7 builds the actual preset library; this just names the set now).
    /// </summary>
    public enum MenuSectionKind
    {
        ContentGrid,
        SearchBar,
        FilterBar,
        StaticButtons,
        ConfirmDialog
    }

    /// <summary>FR-2.2.3: Position Modes.</summary>
    public enum MenuPositionMode
    {
        Static,
        Relative,
        Absolute
    }

    /// <summary>FR-2.2.4: Vertical alignment.</summary>
    public enum MenuAlignVertical
    {
        Top,
        Center,
        Bottom
    }

    /// <summary>FR-2.2.4: Horizontal alignment.</summary>
    public enum MenuAlignHorizontal
    {
        Left,
        Center,
        Right
    }

    /// <summary>FR-2.4.3: Overflow Handling.</summary>
    public enum MenuOverflowMode
    {
        Scroll,
        Hide,
        Wrap
    }

    /// <summary>FR-2.4.4: List Modes.</summary>
    public enum MenuListMode
    {
        Default,
        Linear,
        Grid
    }

    /// <summary>FR-2.4.2: Render Priority (layering when sections overlap).</summary>
    public enum MenuRenderPriority
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// FR-2.4.1 Display Modes, using IMPLEMENTATION_PLAN.md's naming
    /// (NORMAL/DISABLED/HIGHLIGHT/HIDDEN) rather than the older requirements
    /// doc's DISPLAY/HIDE/DISABLED/HIGHLIGHT.
    /// </summary>
    public enum MenuDisplayMode
    {
        Normal,
        Disabled,
        Highlight,
        Hidden
    }

    /// <summary>
    /// DESIGN_REVIEW.md §1 cache-invalidation policy, decided: every VariableBinding
    /// carries exactly one of these, with OnDirty as the default.
    /// </summary>
    public enum VariableRefreshPolicy
    {
        Static,
        OnDirty,
        Ttl
    }

    /// <summary>
    /// InventoryMenu Phase 9 (E5): when a ConditionBinding is evaluated.
    /// Click (the default, Phase 6 behaviour) gates the click; Render is
    /// evaluated on every render pass and hides the item (item-level) or drops
    /// the action (action-level) when it denies - see
    /// docs/specs/inventory-menu/IMPLEMENTATION_PLAN.md "Phase 9".
    /// </summary>
    public enum MenuConditionPhase
    {
        Click,
        Render
    }
}
