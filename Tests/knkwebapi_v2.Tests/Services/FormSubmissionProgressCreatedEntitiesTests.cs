using knkwebapi_v2.Enums;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-162 (D3): the root progress records what its child forms created (CreatedEntitiesJson).
/// It round-trips through the DTOs, and an update that doesn't send it keeps what is recorded.
/// </summary>
public class FormSubmissionProgressCreatedEntitiesTests
{
    private const string Created = "[{\"entityTypeName\":\"District\",\"id\":5,\"label\":\"Oostwijk\",\"sequence\":1}]";

    private readonly IMapper _mapper = new MapperConfiguration(configuration =>
        configuration.AddProfile<FormMappingProfile>()).CreateMapper();

    [Fact]
    public void Mapping_RoundTripsCreatedEntitiesJson()
    {
        var entity = _mapper.Map<FormSubmissionProgress>(new FormSubmissionProgressDto
        {
            FormConfigurationId = "3",
            UserId = "1",
            EntityTypeName = "Street",
            CreatedEntitiesJson = Created
        });
        Assert.Equal(Created, entity.CreatedEntitiesJson);

        Assert.Equal(Created, _mapper.Map<FormSubmissionProgressDto>(entity).CreatedEntitiesJson);
        Assert.Equal(Created, _mapper.Map<FormSubmissionProgressSummaryDto>(entity).CreatedEntitiesJson);
    }

    [Fact]
    public void Mapping_BlankCreatedEntitiesJson_IsNull()
    {
        var entity = _mapper.Map<FormSubmissionProgress>(new FormSubmissionProgressDto
        {
            FormConfigurationId = "3",
            UserId = "1",
            EntityTypeName = "Street",
            CreatedEntitiesJson = "  "
        });

        Assert.Null(entity.CreatedEntitiesJson);
    }

    [Theory]
    [InlineData(null, Created)]   // not sent: keep what is recorded
    [InlineData("[]", "[]")]      // sent empty: cleared
    [InlineData("[{\"id\":9}]", "[{\"id\":9}]")]
    public async Task Update_KeepsCreatedEntitiesUnlessSent(string? sent, string expected)
    {
        var existing = new FormSubmissionProgress { Id = 11, UserId = 1, FormConfigurationId = 3, CreatedEntitiesJson = Created };
        var repo = new Mock<IFormSubmissionProgressRepository>();
        repo.Setup(r => r.GetByIdAsync(11)).ReturnsAsync(existing);
        var service = new FormSubmissionProgressService(
            repo.Object, _mapper, Mock.Of<IFormConfigurationRepository>(), Mock.Of<IDisplayConditionEvaluator>());

        await service.UpdateProgressAsync(11, new FormSubmissionProgressDto
        {
            Id = "11",
            FormConfigurationId = "3",
            UserId = "1",
            EntityTypeName = "Street",
            CurrentStepIndex = 1,
            Status = FormSubmissionStatus.Paused,
            CreatedEntitiesJson = sent
        });

        Assert.Equal(expected, existing.CreatedEntitiesJson);
        repo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }
}
