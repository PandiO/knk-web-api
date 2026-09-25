using System;
using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

// Updated with FieldValidationRule relationship configuration
namespace knkwebapi_v2.Properties;

public partial class KnKDbContext : DbContext
{
    public KnKDbContext()
    {
    }

    public KnKDbContext(DbContextOptions<KnKDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Domain> Domains { get; set; } = null!;
    public virtual DbSet<User> Users { get; set; } = null!;
    public virtual DbSet<LinkCode> LinkCodes { get; set; } = null!;
    public virtual DbSet<Category> Categories { get; set; } = null!;
    public DbSet<FormConfiguration> FormConfigurations { get; set; }
    public DbSet<FormStep> FormSteps { get; set; }
    public DbSet<FormField> FormFields { get; set; }
    public DbSet<FieldValidation> FieldValidations { get; set; }
    public DbSet<FieldValidationRule> FieldValidationRules { get; set; }
    public DbSet<StepCondition> StepConditions { get; set; }
    public DbSet<DisplayConditionGroup> DisplayConditionGroups { get; set; }
    public DbSet<DisplayCondition> DisplayConditions { get; set; }
    public DbSet<FormSubmissionProgress> FormSubmissionProgresses { get; set; }
    
    // DisplayConfiguration DbSets
    public DbSet<DisplayConfiguration> DisplayConfigurations { get; set; }
    public DbSet<DisplaySection> DisplaySections { get; set; }
    public DbSet<DisplayField> DisplayFields { get; set; }
    
    // Entity Type Configuration (admin-configurable entity display properties)
    public DbSet<EntityTypeConfiguration> EntityTypeConfigurations { get; set; }

    // Singleton game settings (global + per-world server behavior)
    public DbSet<GameSettings> GameSettings { get; set; }
    
    public virtual DbSet<Location> Locations { get; set; } = null!;
    public virtual DbSet<Street> Streets { get; set; } = null!;
    public virtual DbSet<Town> Towns { get; set; } = null!;
    public virtual DbSet<District> Districts { get; set; } = null!;
    public virtual DbSet<Structure> Structures { get; set; } = null!;
    public virtual DbSet<GateStructure> GateStructures { get; set; } = null!;
    public virtual DbSet<GateDoor> GateDoors { get; set; } = null!;
    public virtual DbSet<GateBlockSnapshot> GateBlockSnapshots { get; set; } = null!;
    public virtual DbSet<GateOpenedBlockSnapshot> GateOpenedBlockSnapshots { get; set; } = null!;
    public virtual DbSet<ItemBlueprint> ItemBlueprints { get; set; } = null!;
    public virtual DbSet<MinecraftMaterialRef> MinecraftMaterialRefs { get; set; } = null!;
    public virtual DbSet<MinecraftBlockRef> MinecraftBlockRefs { get; set; } = null!;
    public virtual DbSet<MinecraftEnchantmentRef> MinecraftEnchantmentRefs { get; set; } = null!;
    public virtual DbSet<EnchantmentDefinition> EnchantmentDefinitions { get; set; } = null!;
    public virtual DbSet<AbilityDefinition> AbilityDefinitions { get; set; } = null!;
    public virtual DbSet<Grade> Grades { get; set; } = null!;
    public virtual DbSet<Tag> Tags { get; set; } = null!;
    // CategoryTag/ItemBlueprintTag have no DbSet, matching ItemBlueprintDefaultEnchantment's precedent
    // (KnKDbContext.cs OnModelCreating) - a plain composite-key join entity reached only through its
    // parent's navigation property, not its own controller/repository.
    public virtual DbSet<ItemBlueprintOrigin> ItemBlueprintOrigins { get; set; } = null!;
    // Workflow + Tasks
    public virtual DbSet<WorkflowSession> WorkflowSessions { get; set; } = null!;
    public virtual DbSet<StepProgress> StepProgresses { get; set; } = null!;
    public virtual DbSet<WorldTask> WorldTasks { get; set; } = null!;

    // InventoryMenu (docs/specs/inventory-menu/IMPLEMENTATION_PLAN.md Phase 1)
    public virtual DbSet<MenuTemplate> MenuTemplates { get; set; } = null!;
    public virtual DbSet<MenuSectionTemplate> MenuSectionTemplates { get; set; } = null!;
    public virtual DbSet<MenuItemTemplate> MenuItemTemplates { get; set; } = null!;
    public virtual DbSet<VariableBinding> VariableBindings { get; set; } = null!;
    public virtual DbSet<ActionBinding> ActionBindings { get; set; } = null!;
    public virtual DbSet<ConditionBinding> ConditionBindings { get; set; } = null!;

    // User features Phase 1 — permission/rank system (docs/specs/user-features/IMPLEMENTATION_PLAN.md §1)
    public virtual DbSet<PermissionHolder> PermissionHolders { get; set; } = null!;
    public virtual DbSet<PermissionGroup> PermissionGroups { get; set; } = null!;
    public virtual DbSet<PermissionGrant> PermissionGrants { get; set; } = null!;
    public virtual DbSet<UserPermissionGroup> UserPermissionGroups { get; set; } = null!;

    // User features Phase 4 — title/XP track (docs/specs/user-features/IMPLEMENTATION_PLAN.md §4)
    public virtual DbSet<TitleBracket> TitleBrackets { get; set; } = null!;

    // User features Phase 6 — salary system (docs/specs/user-features/IMPLEMENTATION_PLAN.md §6)
    public DbSet<SalaryConfiguration> SalaryConfigurations { get; set; }

    public virtual DbSet<AuditLogEntry> AuditLogEntries { get; set; } = null!;

    // User management — audit log retention policy (docs/specs/user-management/DESIGN.md §7 item 3)
    public DbSet<AuditLogRetentionConfiguration> AuditLogRetentionConfigurations { get; set; } = null!;

    // Siege Phase 1 — banner + minimal clan (docs/specs/siege-minigame/DESIGN.md §3.1–3.2)
    public virtual DbSet<BannerDesign> BannerDesigns { get; set; } = null!;
    public virtual DbSet<BannerLayer> BannerLayers { get; set; } = null!;
    public virtual DbSet<Clan> Clans { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_general_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Domain>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("domains");
        });

        // PermissionHolder TPT base — User/PermissionGroup : PermissionHolder, sharing this table's Id
        // as their own PK, same pattern as Domain/Town/District/Structure below.
        modelBuilder.Entity<PermissionHolder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("permission_holders");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            // Unique constraints on Username, Email, UUID (with null handling)
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Uuid).IsUnique();

            // Relationship to LinkCodes
            entity.HasMany(e => e.LinkCodes)
                .WithOne(lc => lc.User)
                .HasForeignKey(lc => lc.UserId)
                .OnDelete(DeleteBehavior.Restrict); // No cascade; soft-delete handles cleanup
        });

        modelBuilder.Entity<PermissionGroup>(entity =>
        {
            entity.ToTable("permission_groups");

            entity.HasIndex(e => e.Name).IsUnique();

            // Single-parent inheritance chain (DESIGN.md §2.1) — restrict, not cascade: deleting
            // a parent group with live children should fail loudly, not silently orphan them.
            entity.HasOne(g => g.ParentGroup)
                .WithMany(g => g.ChildGroups)
                .HasForeignKey(g => g.ParentGroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PermissionGrant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("permission_grants");

            entity.Property(e => e.Node).IsRequired().HasMaxLength(191);

            // A holder's grant list is meaningless without the holder — cascade.
            entity.HasOne(e => e.Holder)
                .WithMany(h => h.Grants)
                .HasForeignKey(e => e.HolderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Resolution always looks up "this holder's grants" first — index the lookup path.
            entity.HasIndex(e => new { e.HolderId, e.Node });
        });

        modelBuilder.Entity<UserPermissionGroup>(entity =>
        {
            entity.ToTable("user_permission_groups");
            entity.HasKey(e => new { e.UserId, e.PermissionGroupId });

            // Membership rows are meaningless without either side — cascade both ways,
            // matching ItemBlueprintDefaultEnchantment's join-entity precedent.
            entity.HasOne(e => e.User)
                .WithMany(u => u.PermissionGroupMemberships)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PermissionGroup)
                .WithMany(g => g.UserMemberships)
                .HasForeignKey(e => e.PermissionGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TitleBracket>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("title_brackets");

            entity.Property(e => e.MaleName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.FemaleName).IsRequired().HasMaxLength(100);

            // Brackets are resolved by "highest MinExperience <= user's XP" — each threshold
            // must be distinct or resolution would be ambiguous.
            entity.HasIndex(e => e.MinExperience).IsUnique();
        });

        // Siege Phase 1 (docs/specs/siege-minigame/DESIGN.md §3.1–3.2). Layers cascade from their
        // BannerDesign (owned); Clan never cascades into the shared BannerDesign/Town rows (Restrict).
        modelBuilder.Entity<BannerDesign>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("banner_designs");

            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.BaseColor).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<BannerLayer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("banner_layers");

            entity.Property(e => e.PatternKey).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Color).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.BannerDesignId, e.SortOrder });

            entity.HasOne(e => e.BannerDesign)
                .WithMany(d => d.Layers)
                .HasForeignKey(e => e.BannerDesignId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Clan>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("clans");

            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ChatColor).IsRequired().HasMaxLength(20);

            // MySQL unique indexes ignore NULLs, so this enforces "at most one default clan per
            // town" while allowing any number of clans with no default town.
            entity.HasIndex(e => e.DefaultForTownId).IsUnique();

            entity.HasOne(e => e.BannerDesign)
                .WithMany()
                .HasForeignKey(e => e.BannerDesignId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DefaultForTown)
                .WithMany()
                .HasForeignKey(e => e.DefaultForTownId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LinkCode>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("linkcodes");
            
            // Unique constraint on Code - cannot be reused
            entity.HasIndex(e => e.Code).IsUnique();
            
            // Index on ExpiresAt for efficient cleanup queries
            entity.HasIndex(e => e.ExpiresAt);
            
            // FK to User (no cascade - handled at application level)
            entity.HasOne(e => e.User)
                .WithMany(u => u.LinkCodes)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("categories");

            entity.HasOne(c => c.IconMaterialRef)
                .WithMany()
                .HasForeignKey(c => c.IconMaterialRefId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ItemBlueprint>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("item_blueprints");

            entity.HasOne(ib => ib.IconMaterial)
                .WithMany()
                .HasForeignKey(ib => ib.IconMaterialRefId)
                .OnDelete(DeleteBehavior.Restrict);

            // Same precedent as IconMaterialRefId above: a catalog-lookup FK, Restrict so deleting a
            // Category/Grade still in use by an ItemBlueprint fails loudly instead of silently nulling it out.
            entity.HasOne(ib => ib.Category)
                .WithMany()
                .HasForeignKey(ib => ib.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(ib => ib.Grade)
                .WithMany()
                .HasForeignKey(ib => ib.GradeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Grade>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("grades");
        });
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("tags");
        });

        // CategoryTag / ItemBlueprintTag: plain composite-key join entities, no extra columns beyond the
        // two FKs - same shape as ItemBlueprintDefaultEnchantment (KnKDbContext.cs, below), cascading both
        // ways for the same reason: the join row has no meaning without both sides, so deleting either the
        // Category/ItemBlueprint or the Tag should clean up the join row rather than leaving it orphaned.
        modelBuilder.Entity<CategoryTag>()
            .HasKey(ct => new { ct.CategoryId, ct.TagId });

        modelBuilder.Entity<CategoryTag>()
            .HasOne(ct => ct.Category)
            .WithMany(c => c.Tags)
            .HasForeignKey(ct => ct.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CategoryTag>()
            .HasOne(ct => ct.Tag)
            .WithMany()
            .HasForeignKey(ct => ct.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ItemBlueprintTag>()
            .HasKey(it => new { it.ItemBlueprintId, it.TagId });

        modelBuilder.Entity<ItemBlueprintTag>()
            .HasOne(it => it.ItemBlueprint)
            .WithMany(ib => ib.Tags)
            .HasForeignKey(it => it.ItemBlueprintId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ItemBlueprintTag>()
            .HasOne(it => it.Tag)
            .WithMany()
            .HasForeignKey(it => it.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // ItemBlueprintOrigin: independent Id PK (not a composite key - the same Domain can legitimately
        // appear twice in one item's history, see the model's own comment). ItemBlueprintId cascades (the
        // origin history is meaningless without its ItemBlueprint); DomainId restricts (per
        // docs/specs/items/IMPLEMENTATION_PLAN.md §3.2 explicitly, matching IconMaterialRefId's precedent -
        // a Domain that's referenced as an item's origin should not be deletable out from under that history).
        modelBuilder.Entity<ItemBlueprintOrigin>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("item_blueprint_origins");

            entity.HasIndex(e => new { e.ItemBlueprintId, e.SequenceNumber }).IsUnique();

            entity.HasOne(o => o.ItemBlueprint)
                .WithMany(ib => ib.Origins)
                .HasForeignKey(o => o.ItemBlueprintId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.Domain)
                .WithMany()
                .HasForeignKey(o => o.DomainId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("locations");
        });

        base.OnModelCreating(modelBuilder);
        
        // FormConfiguration relationships
        modelBuilder.Entity<FormConfiguration>()
            .HasMany(fc => fc.Steps)
            .WithOne(s => s.FormConfiguration)
            .HasForeignKey(s => s.FormConfigurationId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // FormStep relationships
        modelBuilder.Entity<FormStep>()
            .HasMany(s => s.Fields)
            .WithOne(f => f.FormStep)
            .HasForeignKey(f => f.FormStepId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<FormStep>()
            .HasMany(s => s.StepConditions)
            .WithOne(sc => sc.FormStep)
            .HasForeignKey(sc => sc.FormStepId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // FormStep self-referencing relationship for many-to-many child steps
        modelBuilder.Entity<FormStep>()
            .HasMany(s => s.ChildFormSteps)
            .WithOne(cs => cs.ParentStep)
            .HasForeignKey(cs => cs.ParentStepId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FormStep>()
            .HasOne(s => s.SubConfiguration)
            .WithMany()
            .HasForeignKey(s => s.SubConfigurationId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // FormField relationships
        modelBuilder.Entity<FormField>()
            .HasMany(f => f.Validations)
            .WithOne(v => v.FormField)
            .HasForeignKey(v => v.FormFieldId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<FormField>()
            .HasMany(f => f.ValidationRules)
            .WithOne(vr => vr.FormField)
            .HasForeignKey(vr => vr.FormFieldId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<FormField>()
            .HasOne(f => f.DependsOnField)
            .WithMany(f => f.DependentFields)
            .HasForeignKey(f => f.DependsOnFieldId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<FormField>()
            .HasOne(f => f.SubConfiguration)
            .WithMany()
            .HasForeignKey(f => f.SubConfigurationId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // FieldValidationRule relationships
        modelBuilder.Entity<FieldValidationRule>()
            .HasOne(vr => vr.DependsOnField)
            .WithMany()
            .HasForeignKey(vr => vr.DependsOnFieldId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // FormSubmissionProgress relationships
        modelBuilder.Entity<FormSubmissionProgress>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<FormSubmissionProgress>()
            .HasOne(p => p.FormConfiguration)
            .WithMany(fc => fc.SubmissionProgresses)
            .HasForeignKey(p => p.FormConfigurationId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<FormSubmissionProgress>()
            .HasOne(p => p.ParentProgress)
            .WithMany()
            .HasForeignKey(p => p.ParentProgressId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FormSubmissionProgress>()
            .HasIndex(p => p.ParentProgressId);

        modelBuilder.Entity<FormSubmissionProgress>()
            .HasIndex(p => new { p.Status, p.CompletedAt });
        
        // Indexes for performance
        modelBuilder.Entity<FormConfiguration>()
            .HasIndex(fc => fc.EntityTypeName);
        
        modelBuilder.Entity<FormConfiguration>()
            .HasIndex(fc => fc.ConfigurationGuid)
            .IsUnique();
        
        modelBuilder.Entity<FormStep>()
            .HasIndex(s => s.IsReusable);
        
        modelBuilder.Entity<FormField>()
            .HasIndex(f => f.IsReusable);

        // DisplayCondition relationships
        modelBuilder.Entity<DisplayConditionGroup>()
            .HasOne(g => g.TargetStep)
            .WithMany(s => s.DisplayConditionGroups)
            .HasForeignKey(g => g.TargetStepId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DisplayConditionGroup>()
            .HasOne(g => g.TargetField)
            .WithMany(f => f.DisplayConditionGroups)
            .HasForeignKey(g => g.TargetFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DisplayConditionGroup>()
            .HasMany(g => g.ChildGroups)
            .WithOne(g => g.ParentGroup)
            .HasForeignKey(g => g.ParentGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DisplayConditionGroup>()
            .HasMany(g => g.Conditions)
            .WithOne(c => c.DisplayConditionGroup)
            .HasForeignKey(c => c.DisplayConditionGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: removing a field that other conditions read must fail loudly
        // instead of silently making dependent steps permanently visible.
        modelBuilder.Entity<DisplayCondition>()
            .HasOne(c => c.SourceFormField)
            .WithMany(f => f.UsedInDisplayConditions)
            .HasForeignKey(c => c.SourceFormFieldId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DisplayConditionGroup>()
            .HasIndex(g => g.TargetStepId);

        modelBuilder.Entity<DisplayConditionGroup>()
            .HasIndex(g => g.TargetFieldId);

        modelBuilder.Entity<DisplayCondition>()
            .HasIndex(c => c.SourceFormFieldId);

        // DisplayConfiguration relationships
        modelBuilder.Entity<DisplayConfiguration>()
            .HasMany(dc => dc.Sections)
            .WithOne(ds => ds.DisplayConfiguration)
            .HasForeignKey(ds => ds.DisplayConfigurationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DisplayConfiguration>()
            .HasIndex(dc => new { dc.EntityTypeName, dc.IsDefault })
            .HasDatabaseName("IX_DisplayConfiguration_EntityType_Default");

        modelBuilder.Entity<DisplayConfiguration>()
            .HasIndex(dc => dc.IsDraft)
            .HasDatabaseName("IX_DisplayConfiguration_IsDraft");
        
        // DisplaySection relationships
        modelBuilder.Entity<DisplaySection>()
            .HasMany(ds => ds.Fields)
            .WithOne(df => df.DisplaySection)
            .HasForeignKey(df => df.DisplaySectionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DisplaySection>()
            .HasMany(ds => ds.SubSections)
            .WithOne(ss => ss.ParentSection)
            .HasForeignKey(ss => ss.ParentSectionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DisplaySection>()
            .HasIndex(ds => ds.IsReusable)
            .HasDatabaseName("IX_DisplaySection_IsReusable");

        modelBuilder.Entity<DisplaySection>()
            .HasIndex(ds => ds.ParentSectionId)
            .HasDatabaseName("IX_DisplaySection_ParentSectionId");
        
        // DisplayField indexes
        modelBuilder.Entity<DisplayField>()
            .HasIndex(df => df.IsReusable)
            .HasDatabaseName("IX_DisplayField_IsReusable");

        modelBuilder.Entity<Domain>()
            .HasOne(d => d.Location)
            .WithOne()
            .HasForeignKey<Domain>(d => d.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Street configuration
        modelBuilder.Entity<Street>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("streets");
        });

        // Town configuration
        modelBuilder.Entity<Town>(entity =>
        {
            entity.ToTable("towns");
        });

        // Town-Street many-to-many
        modelBuilder.Entity<Town>()
            .HasMany(t => t.Streets)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "TownStreet",
                j => j.HasOne<Street>().WithMany().HasForeignKey("StreetId"),
                j => j.HasOne<Town>().WithMany().HasForeignKey("TownId"));

        // District configuration
        modelBuilder.Entity<District>(entity =>
        {
            entity.ToTable("districts");
        });

        // District-Town many-to-one (required)
        modelBuilder.Entity<District>()
            .HasOne(d => d.Town)
            .WithMany(t => t.Districts)
            .HasForeignKey(d => d.TownId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // District-Street many-to-many
        modelBuilder.Entity<District>()
            .HasMany(d => d.Streets)
            .WithMany(s => s.Districts)
            .UsingEntity<Dictionary<string, object>>(
                "DistrictStreet",
                j => j.HasOne<Street>().WithMany().HasForeignKey("StreetId"),
                j => j.HasOne<District>().WithMany().HasForeignKey("DistrictId"));

        // Structure configuration
        modelBuilder.Entity<Structure>(entity =>
        {
            entity.ToTable("structures");
        });

        // GateStructure configuration
        modelBuilder.Entity<GateStructure>(entity =>
        {
            entity.ToTable("gate_structures");

            // Persist override enums as strings, matching the non-nullable enum convention below.
            entity.Property(e => e.OpenedStateOverride).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.HealthDisplayModeOverride).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.GateNameDisplayModeOverride).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.StatusDisplayModeOverride).HasConversion<string>().HasMaxLength(50);

            // Foreign key relationships
            entity.HasOne(g => g.IconMaterial)
                .WithMany()
                .HasForeignKey(g => g.IconMaterialRefId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(g => g.GuardSpawnLocations)
                .WithMany()
                .UsingEntity<Dictionary<string, object>>(
                    "gate_structure_guard_spawn_locations",
                    j => j
                        .HasOne<Location>()
                        .WithMany()
                        .HasForeignKey("LocationId")
                        .OnDelete(DeleteBehavior.Cascade),
                    j => j
                        .HasOne<GateStructure>()
                        .WithMany()
                        .HasForeignKey("GateStructureId")
                        .OnDelete(DeleteBehavior.Cascade),
                    j =>
                    {
                        j.HasKey("GateStructureId", "LocationId");
                        j.ToTable("gate_structure_guard_spawn_locations");
                    });

            // One-to-many relationship with GateDoor
            entity.HasMany(g => g.GateDoors)
                .WithOne(d => d.GateStructure)
                .HasForeignKey(d => d.GateStructureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GateDoor configuration - most of what used to be per-GateStructure geometry/animation/
        // state configuration now lives here; see GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md item 5.
        modelBuilder.Entity<GateDoor>(entity =>
        {
            entity.ToTable("gate_doors");

            // Persist enums as strings for DB readability and stable API semantics.
            entity.Property(e => e.GateType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.GeometryDefinitionMode).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.MotionType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.TileEntityPolicy).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.HealthDisplayMode).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.OpenedState).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.FaceDirection).HasConversion<string>().HasMaxLength(50);
            // GateNameDisplayMode/StatusDisplayMode intentionally NOT string-converted here,
            // matching the original GateStructure config (which never applied .HasConversion
            // <string>() to these two, unlike its other enums) - the migration's data backfill
            // copies these columns' existing int values straight across, so changing the storage
            // representation here would corrupt every existing gate's display-mode setting.
            // DoorNameDisplayMode (a brand-new field) kept int-backed too, for consistency with
            // its two siblings above.

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(191);

            // Indexes
            entity.HasIndex(e => e.IsActive)
                .HasDatabaseName("IX_GateDoor_IsActive");

            entity.HasIndex(e => e.GateType)
                .HasDatabaseName("IX_GateDoor_GateType");

            entity.HasIndex(e => e.OpenedState)
                .HasDatabaseName("IX_GateDoor_OpenedState");

            // Name is unique within its parent structure, not globally (decision 5.0-D).
            entity.HasIndex(e => new { e.GateStructureId, e.Name })
                .IsUnique()
                .HasDatabaseName("IX_GateDoor_GateStructureId_Name");

            // Foreign key relationships
            entity.HasOne(d => d.FallbackMaterial)
                .WithMany()
                .HasForeignKey(d => d.FallbackMaterialRefId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.AnchorPoint)
                .WithMany()
                .HasForeignKey(d => d.AnchorPointId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.OpenAnchorPoint)
                .WithMany()
                .HasForeignKey(d => d.OpenAnchorPointId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.ReferencePoint1)
                .WithMany()
                .HasForeignKey(d => d.ReferencePoint1Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.ReferencePoint2)
                .WithMany()
                .HasForeignKey(d => d.ReferencePoint2Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.HingeAxis)
                .WithMany()
                .HasForeignKey(d => d.HingeAxisId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.LeftDoorSeedBlock)
                .WithMany()
                .HasForeignKey(d => d.LeftDoorSeedBlockId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.RightDoorSeedBlock)
                .WithMany()
                .HasForeignKey(d => d.RightDoorSeedBlockId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.InfoDisplayLocation)
                .WithMany()
                .HasForeignKey(d => d.InfoDisplayLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            // One-to-many relationship with GateBlockSnapshot
            entity.HasMany(d => d.BlockSnapshots)
                .WithOne(bs => bs.GateDoor)
                .HasForeignKey(bs => bs.GateDoorId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many relationship with GateOpenedBlockSnapshot
            entity.HasMany(d => d.OpenedBlockSnapshots)
                .WithOne(bs => bs.GateDoor)
                .HasForeignKey(bs => bs.GateDoorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GateBlockSnapshot configuration
        modelBuilder.Entity<GateBlockSnapshot>(entity =>
        {
            entity.ToTable("gate_block_snapshots");

            entity.HasKey(e => e.Id);

            // Indexes for performance
            entity.HasIndex(e => e.GateDoorId)
                .HasDatabaseName("IX_GateBlockSnapshot_GateDoorId");

            entity.HasIndex(e => new { e.GateDoorId, e.SortOrder })
                .HasDatabaseName("IX_GateBlockSnapshot_GateDoorId_SortOrder");

            entity.HasIndex(e => new { e.WorldX, e.WorldY, e.WorldZ })
                .HasDatabaseName("IX_GateBlockSnapshot_WorldCoordinates");

            // Required fields
            entity.Property(e => e.MaterialName)
                .IsRequired()
                .HasMaxLength(191);

            entity.Property(e => e.BlockDataJson)
                .HasMaxLength(1000);

            entity.Property(e => e.TileEntityJson)
                .HasMaxLength(2000);
        });

        // GateOpenedBlockSnapshot configuration - mirrors GateBlockSnapshot above exactly,
        // its own table rather than a shared one, per docs/features/gate-structure-animation/
        // ROTATION_GAP_FILL_DESIGN.md.
        modelBuilder.Entity<GateOpenedBlockSnapshot>(entity =>
        {
            entity.ToTable("gate_opened_block_snapshots");

            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.GateDoorId)
                .HasDatabaseName("IX_GateOpenedBlockSnapshot_GateDoorId");

            entity.HasIndex(e => new { e.GateDoorId, e.SortOrder })
                .HasDatabaseName("IX_GateOpenedBlockSnapshot_GateDoorId_SortOrder");

            entity.HasIndex(e => new { e.WorldX, e.WorldY, e.WorldZ })
                .HasDatabaseName("IX_GateOpenedBlockSnapshot_WorldCoordinates");

            entity.Property(e => e.MaterialName)
                .IsRequired()
                .HasMaxLength(191);

            entity.Property(e => e.BlockDataJson)
                .HasMaxLength(1000);

            entity.Property(e => e.TileEntityJson)
                .HasMaxLength(2000);
        });

        modelBuilder.Entity<MinecraftMaterialRef>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("minecraftmaterialrefs");
            entity.HasIndex(e => e.NamespaceKey).IsUnique();
            entity.Property(e => e.NamespaceKey).IsRequired().HasMaxLength(191);
            entity.Property(e => e.Category).IsRequired();
        });

        modelBuilder.Entity<MinecraftBlockRef>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("minecraftblockrefs");
            entity.Property(e => e.NamespaceKey).IsRequired().HasMaxLength(191);
        });

        modelBuilder.Entity<MinecraftEnchantmentRef>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("minecraftenchantmentrefs");
            entity.HasIndex(e => e.NamespaceKey).IsUnique();
            entity.Property(e => e.NamespaceKey).IsRequired().HasMaxLength(191);
        });

        modelBuilder.Entity<AbilityDefinition>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("AbilityDefinitions");

            entity.Property(e => e.AbilityKey)
                .IsRequired()
                .HasMaxLength(191);

            entity.HasIndex(e => e.EnchantmentDefinitionId)
                .IsUnique();
        });

        // EntityTypeConfiguration model configuration
        modelBuilder.Entity<EntityTypeConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("entity_type_configurations");
            
            entity.Property(e => e.EntityTypeName)
                .IsRequired()
                .HasMaxLength(191);
            
            entity.Property(e => e.IconKey)
                .HasMaxLength(50);
            
            entity.Property(e => e.CustomIconUrl)
                .HasMaxLength(500);
            
            entity.Property(e => e.DisplayColor)
                .HasMaxLength(7);

            entity.Property(e => e.DefaultTableColumnsJson)
                .HasColumnType("longtext");
            
            // CreatedAt and UpdatedAt are set in C# (DateTime.UtcNow), not via SQL defaults
            // This avoids MySQL timezone issues
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime");
            
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime");
            
            entity.HasIndex(e => e.EntityTypeName)
                .IsUnique()
                .HasDatabaseName("IX_EntityTypeConfiguration_EntityTypeName");
        });

        modelBuilder.Entity<SalaryConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("salary_configurations");

            entity.Property(e => e.Id)
                .HasMaxLength(64);
        });

        modelBuilder.Entity<AuditLogRetentionConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("audit_log_retention_configurations");

            entity.Property(e => e.Id)
                .HasMaxLength(64);
        });

        modelBuilder.Entity<GameSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("game_settings");

            entity.Property(e => e.Id)
                .HasMaxLength(64);

            entity.Property(e => e.SettingsVersion)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.JoinAnnouncement)
                .HasColumnType("longtext");

            entity.Property(e => e.LeaveAnnouncement)
                .HasColumnType("longtext");

            entity.Property(e => e.JoinSpawnMode)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.JoinSpawnReferenceJson)
                .HasColumnType("longtext");

            entity.Property(e => e.DefaultRespawnPolicyJson)
                .HasColumnType("longtext");

            entity.Property(e => e.WorldSettingsJson)
                .HasColumnType("longtext");

            entity.Property(e => e.RuntimeWorldsJson)
                .HasColumnType("longtext");

            entity.Property(e => e.RuntimeWorldsLastUpdatedAt)
                .HasColumnType("datetime");

            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime");

            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime");
        });
        // Structure-Street many-to-one (required)
        modelBuilder.Entity<Structure>()
            .HasOne(s => s.Street)
            .WithMany(st => st.Structures)
            .HasForeignKey(s => s.StreetId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Structure-District many-to-one (required)
        modelBuilder.Entity<Structure>()
            .HasOne(s => s.District)
            .WithMany(d => d.Structures)
            .HasForeignKey(s => s.DistrictId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // ItemBlueprintDefaultEnchantment join entity with composite primary key
        modelBuilder.Entity<ItemBlueprintDefaultEnchantment>()
            .HasKey(e => new { e.ItemBlueprintId, e.EnchantmentDefinitionId });

        modelBuilder.Entity<ItemBlueprintDefaultEnchantment>()
            .HasOne(e => e.ItemBlueprint)
            .WithMany(ib => ib.DefaultEnchantments)
            .HasForeignKey(e => e.ItemBlueprintId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ItemBlueprintDefaultEnchantment>()
            .HasOne(e => e.EnchantmentDefinition)
            .WithMany(ed => ed.DefaultForBlueprints)
            .HasForeignKey(e => e.EnchantmentDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EnchantmentDefinition>()
            .HasOne(e => e.AbilityDefinition)
            .WithOne(a => a.EnchantmentDefinition)
            .HasForeignKey<AbilityDefinition>(a => a.EnchantmentDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // WorkflowSession configuration
        modelBuilder.Entity<WorkflowSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("workflow_sessions");

            entity.Property(e => e.SessionGuid)
                .IsRequired();

            entity.Property(e => e.EntityTypeName)
                .HasMaxLength(191);

            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime");

            entity.HasIndex(e => e.SessionGuid)
                .IsUnique();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FormConfiguration)
                .WithMany()
                .HasForeignKey(e => e.FormConfigurationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StepProgress configuration
        modelBuilder.Entity<StepProgress>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("step_progress");

            entity.Property(e => e.StepKey)
                .IsRequired()
                .HasMaxLength(191);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("datetime");

            entity.HasIndex(e => new { e.WorkflowSessionId, e.StepKey })
                .IsUnique();

            entity.HasOne(e => e.WorkflowSession)
                .WithMany(s => s.Steps)
                .HasForeignKey(e => e.WorkflowSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // WorldTask configuration
        modelBuilder.Entity<WorldTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("world_tasks");

            entity.Property(e => e.TaskType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.PayloadJson)
                .HasColumnType("longtext");

            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("datetime");

            entity.HasIndex(e => new { e.WorkflowSessionId, e.Status });
            entity.HasIndex(e => e.AssignedUserId);

            entity.HasOne(e => e.WorkflowSession)
                .WithMany(s => s.WorldTasks)
                .HasForeignKey(e => e.WorkflowSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssignedUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // InventoryMenu (docs/specs/inventory-menu/IMPLEMENTATION_PLAN.md Phase 1)
        modelBuilder.Entity<MenuTemplate>(entity =>
        {
            entity.ToTable("menu_templates");

            entity.Property(e => e.Key).IsRequired().HasMaxLength(191);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(191);
            entity.Property(e => e.Growth).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasIndex(e => e.Key).IsUnique();

            entity.HasOne(e => e.BackgroundMaterial)
                .WithMany()
                .HasForeignKey(e => e.BackgroundMaterialRefId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Sections)
                .WithOne(s => s.MenuTemplate)
                .HasForeignKey(s => s.MenuTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MenuSectionTemplate>(entity =>
        {
            entity.ToTable("menu_section_templates");

            entity.Property(e => e.Name).IsRequired().HasMaxLength(191);
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.PositionMode).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.AlignVertical).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.AlignHorizontal).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Overflow).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ListMode).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ContentSourceId).HasMaxLength(191);
            entity.Property(e => e.ContentSourceParamsJson).HasColumnType("longtext");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            // Name is unique within its parent template, not globally.
            entity.HasIndex(e => new { e.MenuTemplateId, e.Name })
                .IsUnique()
                .HasDatabaseName("IX_MenuSectionTemplate_MenuTemplateId_Name");

            entity.HasIndex(e => new { e.MenuTemplateId, e.SortOrder })
                .HasDatabaseName("IX_MenuSectionTemplate_MenuTemplateId_SortOrder");

            entity.HasMany(e => e.Items)
                .WithOne(i => i.MenuSectionTemplate)
                .HasForeignKey(i => i.MenuSectionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MenuItemTemplate>(entity =>
        {
            entity.ToTable("menu_item_templates");

            entity.Property(e => e.DisplayMode).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasIndex(e => new { e.MenuSectionTemplateId, e.SortOrder })
                .HasDatabaseName("IX_MenuItemTemplate_MenuSectionTemplateId_SortOrder");

            entity.HasOne(e => e.Material)
                .WithMany()
                .HasForeignKey(e => e.MaterialRefId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VariableBinding>(entity =>
        {
            entity.ToTable("menu_variable_bindings");

            entity.Property(e => e.TargetProperty).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Expression).IsRequired().HasMaxLength(500);
            entity.Property(e => e.RefreshPolicy).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasOne(e => e.MenuSectionTemplate)
                .WithMany(s => s.VariableBindings)
                .HasForeignKey(e => e.MenuSectionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.MenuItemTemplate)
                .WithMany(i => i.VariableBindings)
                .HasForeignKey(e => e.MenuItemTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.MenuSectionTemplateId);
            entity.HasIndex(e => e.MenuItemTemplateId);
        });

        modelBuilder.Entity<ActionBinding>(entity =>
        {
            entity.ToTable("menu_action_bindings");

            entity.Property(e => e.ActionTypeId).IsRequired().HasMaxLength(191);
            entity.Property(e => e.ParamsJson).HasColumnType("longtext");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasOne(e => e.MenuItemTemplate)
                .WithMany(i => i.Actions)
                .HasForeignKey(e => e.MenuItemTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.MenuItemTemplateId, e.SortOrder })
                .HasDatabaseName("IX_ActionBinding_MenuItemTemplateId_SortOrder");
        });

        modelBuilder.Entity<ConditionBinding>(entity =>
        {
            entity.ToTable("menu_condition_bindings");

            entity.Property(e => e.ConditionTypeId).IsRequired().HasMaxLength(191);
            entity.Property(e => e.Phase).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ParamsJson).HasColumnType("longtext");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");

            entity.HasOne(e => e.MenuItemTemplate)
                .WithMany(i => i.Conditions)
                .HasForeignKey(e => e.MenuItemTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict: an action being removed goes through its own item's cascade,
            // not through the condition that merely scopes to it.
            entity.HasOne(e => e.ActionBinding)
                .WithMany(a => a.Conditions)
                .HasForeignKey(e => e.ActionBindingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.MenuItemTemplateId, e.SortOrder })
                .HasDatabaseName("IX_ConditionBinding_MenuItemTemplateId_SortOrder");
            entity.HasIndex(e => e.ActionBindingId);
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("audit_log_entries");

            entity.Property(e => e.Action)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Details).HasColumnType("longtext");

            // The read path (GET /api/audit-log) always filters by one of these plus orders by
            // Timestamp descending — see DESIGN.md §4/IMPLEMENTATION_PLAN.md Phase 2.
            entity.HasIndex(e => new { e.TargetUserId, e.Timestamp });
            entity.HasIndex(e => new { e.ActorUserId, e.Timestamp });
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
