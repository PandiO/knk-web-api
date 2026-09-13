using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories
{
    public class FormSubmissionProgressRepositoryTests
    {
        private readonly KnKDbContext _context;
        private readonly FormSubmissionProgressRepository _repository;

        public FormSubmissionProgressRepositoryTests()
        {
            var options = new DbContextOptionsBuilder<KnKDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new KnKDbContext(options);
            _repository = new FormSubmissionProgressRepository(_context);
        }

        [Fact]
        public async Task DeleteCompletedOlderThanAsync_DeletesStaleRootAndDescendantsInOrder()
        {
            // Arrange
            var user = new User
            {
                Username = "retention-user",
                Email = "retention@example.com",
                PasswordHash = "hash"
            };

            var formConfig = new FormConfiguration
            {
                Name = "Test Progress Form",
                EntityTypeName = "TestEntity",
                IsDefault = true
            };

            _context.Users.Add(user);
            _context.FormConfigurations.Add(formConfig);
            await _context.SaveChangesAsync();

            var cutoff = DateTime.UtcNow.AddDays(-14);

            var parent = new FormSubmissionProgress
            {
                UserId = user.Id,
                FormConfigurationId = formConfig.Id,
                Status = "Completed",
                CompletedAt = cutoff.AddDays(-10),
                ParentProgressId = null
            };

            var child = new FormSubmissionProgress
            {
                UserId = user.Id,
                FormConfigurationId = formConfig.Id,
                Status = "Completed",
                CompletedAt = cutoff.AddDays(2),
                ParentProgressId = parent.Id
            };

            _context.FormSubmissionProgresses.Add(parent);
            await _context.SaveChangesAsync();

            child.ParentProgressId = parent.Id;
            _context.FormSubmissionProgresses.Add(child);
            await _context.SaveChangesAsync();

            // Act
            var deletedCount = await _repository.DeleteCompletedOlderThanAsync(cutoff);

            // Assert
            deletedCount.Should().Be(2);
            (await _context.FormSubmissionProgresses.CountAsync()).Should().Be(0);
        }

        /// <summary>
        /// Seeds a user + "GateDoor"-typed FormConfiguration + one FormSubmissionProgress row
        /// whose CurrentStepDataJson/AllStepsDataJson carry the given raw JSON, so each property-
        /// filter test can focus on exactly the shape it's checking.
        /// </summary>
        private async Task<FormSubmissionProgress> SeedProgressAsync(
            string currentStepDataJson, string allStepsDataJson, string status = "Paused", string? username = null)
        {
            var user = new User
            {
                Username = username ?? $"user-{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@example.com",
                PasswordHash = "hash"
            };
            var formConfig = new FormConfiguration
            {
                Name = "Gate Door Configuration",
                EntityTypeName = "GateDoor",
                IsDefault = true
            };
            _context.Users.Add(user);
            _context.FormConfigurations.Add(formConfig);
            await _context.SaveChangesAsync();

            var progress = new FormSubmissionProgress
            {
                UserId = user.Id,
                FormConfigurationId = formConfig.Id,
                Status = status,
                CurrentStepDataJson = currentStepDataJson,
                AllStepsDataJson = allStepsDataJson
            };
            _context.FormSubmissionProgresses.Add(progress);
            await _context.SaveChangesAsync();

            return progress;
        }

        [Fact]
        public async Task GetByEntityTypeNameAsync_MatchesPropertyInCurrentStepData()
        {
            await SeedProgressAsync(
                currentStepDataJson: "{\"GateStructureId\": {\"id\": 14, \"name\": \"Northern Gate\"}, \"Name\": \"New Door\"}",
                allStepsDataJson: "{}");

            var results = await _repository.GetByEntityTypeNameAsync("GateDoor", null, "GateStructureId", "14");

            results.Should().ContainSingle();
        }

        [Fact]
        public async Task GetByEntityTypeNameAsync_MatchesPropertyNestedInAnyStepOfAllStepsData()
        {
            await SeedProgressAsync(
                currentStepDataJson: "{}",
                allStepsDataJson: "{\"Step0\": {\"Name\": \"New Door\"}, \"Step1\": {\"GateStructureId\": {\"id\": 14}}}");

            var results = await _repository.GetByEntityTypeNameAsync("GateDoor", null, "GateStructureId", "14");

            results.Should().ContainSingle();
        }

        [Fact]
        public async Task GetByEntityTypeNameAsync_MatchesBarePrimitiveValue()
        {
            await SeedProgressAsync(
                currentStepDataJson: "{\"GateStructureId\": 14}",
                allStepsDataJson: "{}");

            var results = await _repository.GetByEntityTypeNameAsync("GateDoor", null, "GateStructureId", "14");

            results.Should().ContainSingle();
        }

        [Fact]
        public async Task GetByEntityTypeNameAsync_FieldNameMatchIsCaseInsensitive()
        {
            await SeedProgressAsync(
                currentStepDataJson: "{\"gatestructureid\": {\"id\": 14}}",
                allStepsDataJson: "{}");

            var results = await _repository.GetByEntityTypeNameAsync("GateDoor", null, "GateStructureId", "14");

            results.Should().ContainSingle();
        }

        [Fact]
        public async Task GetByEntityTypeNameAsync_ExcludesNonMatchingValue()
        {
            await SeedProgressAsync(
                currentStepDataJson: "{\"GateStructureId\": {\"id\": 13}}",
                allStepsDataJson: "{}");

            var results = await _repository.GetByEntityTypeNameAsync("GateDoor", null, "GateStructureId", "14");

            results.Should().BeEmpty();
        }

        [Fact]
        public async Task GetByEntityTypeNameAsync_WithoutPropertyFilter_ReturnsAllMatchingEntityType()
        {
            await SeedProgressAsync(
                currentStepDataJson: "{\"GateStructureId\": {\"id\": 13}}",
                allStepsDataJson: "{}");
            await SeedProgressAsync(
                currentStepDataJson: "{\"GateStructureId\": {\"id\": 14}}",
                allStepsDataJson: "{}");

            var results = await _repository.GetByEntityTypeNameAsync("GateDoor", null);

            results.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetByEntityTypeNameAsync_IncludesCreatingUser()
        {
            var seeded = await SeedProgressAsync(
                currentStepDataJson: "{\"GateStructureId\": {\"id\": 14}}",
                allStepsDataJson: "{}",
                username: "the-creator");

            var results = await _repository.GetByEntityTypeNameAsync("GateDoor", null, "GateStructureId", "14");

            var result = results.Should().ContainSingle().Subject;
            result.User.Should().NotBeNull();
            result.User!.Username.Should().Be("the-creator");
        }
    }
}
