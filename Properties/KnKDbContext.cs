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
    public virtual DbSet<GateBlockSnapshot> GateBlockSnapshots { get; set; } = null!;
    public virtual DbSet<GateOpenedBlockSnapshot> GateOpenedBlockSnapshots { get; set; } = null!;
    public virtual DbSet<ItemBlueprint> ItemBlueprints { get; set; } = null!;
    public virtual DbSet<MinecraftMaterialRef> MinecraftMaterialRefs { get; set; } = null!;
    public virtual DbSet<MinecraftBlockRef> MinecraftBlockRefs { get; set; } = null!;
    public virtual DbSet<MinecraftEnchantmentRef> MinecraftEnchantmentRefs { get; set; } = null!;
    public virtual DbSet<EnchantmentDefinition> EnchantmentDefinitions { get; set; } = null!;
    public virtual DbSet<AbilityDefinition> AbilityDefinitions { get; set; } = null!;
    // Workflow + Tasks
    public virtual DbSet<WorkflowSession> WorkflowSessions { get; set; } = null!;
    public virtual DbSet<StepProgress> StepProgresses { get; set; } = null!;
    public virtual DbSet<WorldTask> WorldTasks { get; set; } = null!;

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

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
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

            // Persist enums as strings for DB readability and stable API semantics.
            entity.Property(e => e.GateType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.GeometryDefinitionMode).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.MotionType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.TileEntityPolicy).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.HealthDisplayMode).HasConversion<string>().HasMaxLength(50);
            
            // Indexes
            entity.HasIndex(e => e.IsActive)
                .HasDatabaseName("IX_GateStructure_IsActive");
            
            entity.HasIndex(e => e.GateType)
                .HasDatabaseName("IX_GateStructure_GateType");
            
            entity.HasIndex(e => e.IsOpened)
                .HasDatabaseName("IX_GateStructure_IsOpened");
            
            // Foreign key relationships
            entity.HasOne(g => g.IconMaterial)
                .WithMany()
                .HasForeignKey(g => g.IconMaterialRefId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(g => g.FallbackMaterial)
                .WithMany()
                .HasForeignKey(g => g.FallbackMaterialRefId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.AnchorPoint)
                .WithMany()
                .HasForeignKey(g => g.AnchorPointId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.OpenAnchorPoint)
                .WithMany()
                .HasForeignKey(g => g.OpenAnchorPointId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.ReferencePoint1)
                .WithMany()
                .HasForeignKey(g => g.ReferencePoint1Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.ReferencePoint2)
                .WithMany()
                .HasForeignKey(g => g.ReferencePoint2Id)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.HingeAxis)
                .WithMany()
                .HasForeignKey(g => g.HingeAxisId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.LeftDoorSeedBlock)
                .WithMany()
                .HasForeignKey(g => g.LeftDoorSeedBlockId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(g => g.RightDoorSeedBlock)
                .WithMany()
                .HasForeignKey(g => g.RightDoorSeedBlockId)
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
            
            // One-to-many relationship with GateBlockSnapshot
            entity.HasMany(g => g.BlockSnapshots)
                .WithOne(bs => bs.GateStructure)
                .HasForeignKey(bs => bs.GateStructureId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many relationship with GateOpenedBlockSnapshot
            entity.HasMany(g => g.OpenedBlockSnapshots)
                .WithOne(bs => bs.GateStructure)
                .HasForeignKey(bs => bs.GateStructureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GateBlockSnapshot configuration
        modelBuilder.Entity<GateBlockSnapshot>(entity =>
        {
            entity.ToTable("gate_block_snapshots");
            
            entity.HasKey(e => e.Id);
            
            // Indexes for performance
            entity.HasIndex(e => e.GateStructureId)
                .HasDatabaseName("IX_GateBlockSnapshot_GateStructureId");
            
            entity.HasIndex(e => new { e.GateStructureId, e.SortOrder })
                .HasDatabaseName("IX_GateBlockSnapshot_GateId_SortOrder");

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

            entity.HasIndex(e => e.GateStructureId)
                .HasDatabaseName("IX_GateOpenedBlockSnapshot_GateStructureId");

            entity.HasIndex(e => new { e.GateStructureId, e.SortOrder })
                .HasDatabaseName("IX_GateOpenedBlockSnapshot_GateId_SortOrder");

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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
