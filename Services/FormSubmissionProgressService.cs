using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Dtos;
using AutoMapper;

namespace knkwebapi_v2.Services
{
    public class FormSubmissionProgressService : IFormSubmissionProgressService
    {
        private readonly IFormSubmissionProgressRepository _repo;
        private readonly IMapper _mapper;
        private readonly IFormConfigurationRepository _configRepo;
        private readonly IDisplayConditionEvaluator _displayConditionEvaluator;

        public FormSubmissionProgressService(
            IFormSubmissionProgressRepository repo,
            IMapper mapper,
            IFormConfigurationRepository configRepo,
            IDisplayConditionEvaluator displayConditionEvaluator)
        {
            _repo = repo;
            _mapper = mapper;
            _configRepo = configRepo;
            _displayConditionEvaluator = displayConditionEvaluator;
        }

        public async Task<IEnumerable<FormSubmissionProgressDto>> GetByEntityTypeNameAsync(string entityTypeName, int? userId)
        {
            if (string.IsNullOrWhiteSpace(entityTypeName)) return new List<FormSubmissionProgressDto>();
            var list = await _repo.GetByEntityTypeNameAsync(entityTypeName, userId);
            return _mapper.Map<IEnumerable<FormSubmissionProgressDto>>(list);
        }

        public async Task<IEnumerable<FormSubmissionProgressSummaryDto>> GetSummaryByEntityTypeNameAsync(
            string entityTypeName, int? userId, string? propertyName = null, string? propertyValue = null)
        {
            if (string.IsNullOrWhiteSpace(entityTypeName)) return new List<FormSubmissionProgressSummaryDto>();
            var list = await _repo.GetByEntityTypeNameAsync(entityTypeName, userId, propertyName, propertyValue);
            return _mapper.Map<IEnumerable<FormSubmissionProgressSummaryDto>>(list);
        }

        public async Task<IEnumerable<FormSubmissionProgressDto>> GetByUserIdAsync(int userId)
        {
            if (userId <= 0) return new List<FormSubmissionProgressDto>();
            var list = await _repo.GetByUserIdAsync(userId);
            return _mapper.Map<IEnumerable<FormSubmissionProgressDto>>(list);
        }

        public async Task<FormSubmissionProgressDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return entity == null ? null : _mapper.Map<FormSubmissionProgressDto>(entity);
        }

        public async Task<FormSubmissionProgressDto> SaveProgressAsync(FormSubmissionProgressDto progress)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (string.IsNullOrWhiteSpace(progress.UserId)) throw new ArgumentException("UserId is required.", nameof(progress));
            if (string.IsNullOrWhiteSpace(progress.FormConfigurationId)) throw new ArgumentException("FormConfigurationId is required.", nameof(progress));

            // Validate UserId can be parsed to int
            if (!int.TryParse(progress.UserId, out var userId) || userId <= 0)
                throw new ArgumentException("UserId must be a valid positive integer.", nameof(progress));
            
            // Validate FormConfigurationId can be parsed to int
            if (!int.TryParse(progress.FormConfigurationId, out var configId) || configId <= 0)
                throw new ArgumentException("FormConfigurationId must be a valid positive integer.", nameof(progress));
            if (progress != null)
            {
                progress.UpdatedAt = DateTime.UtcNow.ToString("o");
            }
            var entity = _mapper.Map<FormSubmissionProgress>(progress);
            await _repo.AddAsync(entity);
            return _mapper.Map<FormSubmissionProgressDto>(entity);
        }

        public async Task<FormSubmissionProgressDto> UpdateProgressAsync(int id, FormSubmissionProgressDto progress)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"FormSubmissionProgress with id {id} not found.");

            var incoming = _mapper.Map<FormSubmissionProgress>(progress);
            existing.CurrentStepIndex = incoming.CurrentStepIndex;
            existing.CurrentStepDataJson = incoming.CurrentStepDataJson;
            existing.AllStepsDataJson = incoming.AllStepsDataJson;
            existing.Status = incoming.Status;
            existing.UpdatedAt = DateTime.UtcNow;
            if (string.Equals(incoming.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                existing.CompletedAt = DateTime.UtcNow;
                existing.AllStepsDataJson = await StripHiddenValuesAsync(
                    existing.FormConfigurationId, existing.AllStepsDataJson);
            }

            await _repo.UpdateAsync(existing);
            return _mapper.Map<FormSubmissionProgressDto>(existing);
        }

        /// <summary>
        /// Removes values that belong to a step or field the display conditions hide. The client
        /// already does this, but a completed submission is the last point where the server can
        /// guarantee a toggled-away branch does not end up in the stored result.
        /// </summary>
        private async Task<string> StripHiddenValuesAsync(int formConfigurationId, string? allStepsDataJson)
        {
            if (string.IsNullOrWhiteSpace(allStepsDataJson)) return allStepsDataJson ?? "{}";

            var config = await _configRepo.GetByIdAsync(formConfigurationId);
            if (config == null) return allStepsDataJson;

            Dictionary<string, Dictionary<string, JsonElement>>? perStep;
            try
            {
                perStep = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(allStepsDataJson);
            }
            catch (JsonException)
            {
                return allStepsDataJson;
            }

            if (perStep == null) return allStepsDataJson;

            var flattened = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var stepValues in perStep.Values)
            {
                foreach (var pair in stepValues)
                {
                    flattened[pair.Key] = pair.Value;
                }
            }

            var visible = _displayConditionEvaluator.FilterVisibleValues(config, flattened);

            var filtered = perStep.ToDictionary(
                stepEntry => stepEntry.Key,
                stepEntry => stepEntry.Value
                    .Where(pair => visible.ContainsKey(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value));

            return JsonSerializer.Serialize(filtered);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"FormSubmissionProgress with id {id} not found.");

            await _repo.DeleteAsync(id);
        }
    }
}
