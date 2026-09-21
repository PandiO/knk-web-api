using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class MenuTemplateService : IMenuTemplateService
    {
        private readonly IMenuTemplateRepository _repo;
        private readonly IMinecraftMaterialRefRepository _materialRepo;
        private readonly IMapper _mapper;

        public MenuTemplateService(
            IMenuTemplateRepository repo,
            IMinecraftMaterialRefRepository materialRepo,
            IMapper mapper)
        {
            _repo = repo;
            _materialRepo = materialRepo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<MenuTemplateListDto>> GetAllAsync()
        {
            var items = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<MenuTemplateListDto>>(items);
        }

        public async Task<MenuTemplateDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return entity == null ? null : _mapper.Map<MenuTemplateDto>(entity);
        }

        public async Task<MenuTemplateDto?> GetByKeyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var entity = await _repo.GetByKeyAsync(key);
            return entity == null ? null : _mapper.Map<MenuTemplateDto>(entity);
        }

        public async Task<MenuTemplateDto> CreateAsync(MenuTemplateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Key)) throw new ArgumentException("Key is required.", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));

            var existingByKey = await _repo.GetByKeyAsync(dto.Key);
            if (existingByKey != null)
                throw new ArgumentException($"MenuTemplate with key '{dto.Key}' already exists.");

            await ValidateSectionNamesUniqueAsync(dto.Sections);
            await ValidateMaterialRefAsync(dto.BackgroundMaterialRefId, "BackgroundMaterialRefId");

            var entity = new MenuTemplate
            {
                Key = dto.Key,
                Name = dto.Name,
                Description = dto.Description,
                Height = dto.Height,
                Growth = ParseEnum<MenuGrowthMode>(dto.Growth, nameof(dto.Growth)),
                BackgroundMaterialRefId = dto.BackgroundMaterialRefId,
            };

            foreach (var sectionDto in dto.Sections)
                entity.Sections.Add(await BuildSectionAsync(sectionDto));

            await _repo.AddAsync(entity);
            return _mapper.Map<MenuTemplateDto>(entity);
        }

        public async Task UpdateAsync(int id, MenuTemplateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Key)) throw new ArgumentException("Key is required.", nameof(dto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"MenuTemplate with id {id} not found.");

            var keyOwner = await _repo.GetByKeyAsync(dto.Key);
            if (keyOwner != null && keyOwner.Id != id)
                throw new ArgumentException($"MenuTemplate with key '{dto.Key}' already exists.");

            await ValidateSectionNamesUniqueAsync(dto.Sections);
            await ValidateMaterialRefAsync(dto.BackgroundMaterialRefId, "BackgroundMaterialRefId");

            existing.Key = dto.Key;
            existing.Name = dto.Name;
            existing.Description = dto.Description;
            existing.Height = dto.Height;
            existing.Growth = ParseEnum<MenuGrowthMode>(dto.Growth, nameof(dto.Growth));
            existing.BackgroundMaterialRefId = dto.BackgroundMaterialRefId;
            existing.UpdatedAt = DateTime.UtcNow;

            // Full-replace strategy for the nested tree (mirrors ItemBlueprintService's
            // handling of DefaultEnchantments): templates are authored as one unit for
            // now, so there's no per-child diffing to preserve.
            existing.Sections.Clear();
            foreach (var sectionDto in dto.Sections)
                existing.Sections.Add(await BuildSectionAsync(sectionDto));

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"MenuTemplate with id {id} not found.");

            await _repo.DeleteAsync(id);
        }

        private async Task<MenuSectionTemplate> BuildSectionAsync(MenuSectionTemplateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("MenuSectionTemplate.Name is required.");

            var section = new MenuSectionTemplate
            {
                Name = dto.Name,
                Kind = ParseEnum<MenuSectionKind>(dto.Kind, nameof(dto.Kind)),
                SortOrder = dto.SortOrder,
                DisplaySlot = dto.DisplaySlot,
                Width = dto.Width,
                Height = dto.Height,
                PositionMode = ParseEnum<MenuPositionMode>(dto.PositionMode, nameof(dto.PositionMode)),
                AlignVertical = ParseEnum<MenuAlignVertical>(dto.AlignVertical, nameof(dto.AlignVertical)),
                AlignHorizontal = ParseEnum<MenuAlignHorizontal>(dto.AlignHorizontal, nameof(dto.AlignHorizontal)),
                Overflow = ParseEnum<MenuOverflowMode>(dto.Overflow, nameof(dto.Overflow)),
                ListMode = ParseEnum<MenuListMode>(dto.ListMode, nameof(dto.ListMode)),
                Priority = ParseEnum<MenuRenderPriority>(dto.Priority, nameof(dto.Priority)),
                VisibilityPermission = dto.VisibilityPermission,
            };

            foreach (var bindingDto in dto.VariableBindings)
                section.VariableBindings.Add(BuildVariableBinding(bindingDto));

            foreach (var itemDto in dto.Items)
                section.Items.Add(await BuildItemAsync(itemDto));

            return section;
        }

        private async Task<MenuItemTemplate> BuildItemAsync(MenuItemTemplateDto dto)
        {
            await ValidateMaterialRefAsync(dto.MaterialRefId, "MaterialRefId");

            var item = new MenuItemTemplate
            {
                SortOrder = dto.SortOrder,
                SlotOverride = dto.SlotOverride,
                MaterialRefId = dto.MaterialRefId,
                Amount = dto.Amount,
                ChatColorName = dto.ChatColorName,
                ChatColorDescription = dto.ChatColorDescription,
                DisplayMode = ParseEnum<MenuDisplayMode>(dto.DisplayMode, nameof(dto.DisplayMode)),
                VisibilityPermission = dto.VisibilityPermission,
                ActionPermission = dto.ActionPermission,
            };

            foreach (var bindingDto in dto.VariableBindings)
                item.VariableBindings.Add(BuildVariableBinding(bindingDto));

            foreach (var conditionDto in dto.Conditions)
                item.Conditions.Add(BuildConditionBinding(conditionDto));

            foreach (var actionDto in dto.Actions)
            {
                if (string.IsNullOrWhiteSpace(actionDto.ActionTypeId))
                    throw new ArgumentException("ActionBinding.ActionTypeId is required.");

                var action = new ActionBinding
                {
                    ActionTypeId = actionDto.ActionTypeId,
                    ParamsJson = string.IsNullOrWhiteSpace(actionDto.ParamsJson) ? "{}" : actionDto.ParamsJson,
                    SortOrder = actionDto.SortOrder,
                };

                foreach (var conditionDto in actionDto.Conditions)
                {
                    var condition = BuildConditionBinding(conditionDto);
                    action.Conditions.Add(condition);
                    // ConditionBinding.MenuItemTemplateId is a required FK, so an
                    // action-scoped condition must also be reachable via the item's
                    // own Conditions navigation for EF's fixup to populate it -
                    // membership in action.Conditions alone only sets ActionBindingId.
                    item.Conditions.Add(condition);
                }

                item.Actions.Add(action);
            }

            return item;
        }

        private static VariableBinding BuildVariableBinding(VariableBindingDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TargetProperty))
                throw new ArgumentException("VariableBinding.TargetProperty is required.");
            if (string.IsNullOrWhiteSpace(dto.Expression))
                throw new ArgumentException("VariableBinding.Expression is required.");

            var refreshPolicy = ParseEnum<VariableRefreshPolicy>(dto.RefreshPolicy, nameof(dto.RefreshPolicy));
            if (refreshPolicy == VariableRefreshPolicy.Ttl && (dto.TtlTicks == null || dto.TtlTicks <= 0))
                throw new ArgumentException("VariableBinding.TtlTicks must be a positive number when RefreshPolicy is Ttl.");

            return new VariableBinding
            {
                TargetProperty = dto.TargetProperty,
                SortOrder = dto.SortOrder,
                Expression = dto.Expression,
                RefreshPolicy = refreshPolicy,
                TtlTicks = refreshPolicy == VariableRefreshPolicy.Ttl ? dto.TtlTicks : null,
            };
        }

        private static ConditionBinding BuildConditionBinding(ConditionBindingDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ConditionTypeId))
                throw new ArgumentException("ConditionBinding.ConditionTypeId is required.");

            return new ConditionBinding
            {
                ConditionTypeId = dto.ConditionTypeId,
                ParamsJson = string.IsNullOrWhiteSpace(dto.ParamsJson) ? "{}" : dto.ParamsJson,
                SortOrder = dto.SortOrder,
            };
        }

        private async Task ValidateMaterialRefAsync(int? materialRefId, string fieldName)
        {
            if (materialRefId is null or <= 0) return;
            var material = await _materialRepo.GetByIdAsync(materialRefId.Value);
            if (material == null)
                throw new ArgumentException($"MinecraftMaterialRef with id {materialRefId} not found ({fieldName}).");
        }

        private static Task ValidateSectionNamesUniqueAsync(List<MenuSectionTemplateDto> sections)
        {
            var duplicate = sections
                .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new ArgumentException($"MenuSectionTemplate name '{duplicate.Key}' is not unique within this MenuTemplate.");
            return Task.CompletedTask;
        }

        private static TEnum ParseEnum<TEnum>(string value, string fieldName) where TEnum : struct, Enum
        {
            if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
                throw new ArgumentException($"'{value}' is not a valid {typeof(TEnum).Name} for {fieldName}.");
            return result;
        }
    }
}
