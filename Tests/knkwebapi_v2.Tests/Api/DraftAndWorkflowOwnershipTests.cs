using AutoMapper;
using KnKWebAPI.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// PandiO/knk-workspace#186: form drafts and workflow sessions belong to the logged-in caller (from the JWT), not to
/// the userId the web app sends; "my drafts" lists are filtered by the caller.
/// </summary>
public class DraftAndWorkflowOwnershipTests
{
    private readonly IMapper _mapper = new MapperConfiguration(configuration =>
        configuration.AddProfile<FormMappingProfile>()).CreateMapper();

    private static FormSubmissionProgressController DraftController(IFormSubmissionProgressService service, HttpContext http) =>
        new(service) { ControllerContext = new ControllerContext { HttpContext = http } };

    private static WorkflowsController WorkflowController(IWorkflowService service, HttpContext http) =>
        new(service) { ControllerContext = new ControllerContext { HttpContext = http } };

    private static FormSubmissionProgressDto Draft(string? userId) => new()
    {
        FormConfigurationId = "3",
        UserId = userId,
        EntityTypeName = "Town",
        CurrentStepIndex = 0
    };

    [Theory]
    [InlineData("1")]
    [InlineData(null)]
    [InlineData("99")]
    public async Task SaveProgress_OwnerIsTheCaller_WhateverTheBodySays(string? sentUserId)
    {
        var repo = new Mock<IFormSubmissionProgressRepository>();
        FormSubmissionProgress? stored = null;
        repo.Setup(r => r.AddAsync(It.IsAny<FormSubmissionProgress>()))
            .Callback<FormSubmissionProgress>(p => { p.Id = 40; stored = p; })
            .Returns(Task.CompletedTask);
        var service = new FormSubmissionProgressService(
            repo.Object, _mapper, Mock.Of<IFormConfigurationRepository>(), Mock.Of<IDisplayConditionEvaluator>());

        var result = await DraftController(service, ServiceAuthTestHelper.WebUser(7)).SaveProgress(Draft(sentUserId));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal("7", Assert.IsType<FormSubmissionProgressDto>(created.Value).UserId);
        Assert.Equal(7, stored!.UserId);
    }

    [Fact]
    public async Task SaveProgress_WithoutALoggedInUser_IsRefusedAndNothingIsStored()
    {
        var service = new Mock<IFormSubmissionProgressService>();

        var result = await DraftController(service.Object, ServiceAuthTestHelper.Anonymous()).SaveProgress(Draft("1"));

        Assert.IsType<UnauthorizedObjectResult>(result);
        service.Verify(s => s.SaveProgressAsync(It.IsAny<FormSubmissionProgressDto>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SaveProgressAsync_RejectsAnInvalidOwner()
    {
        var service = new FormSubmissionProgressService(
            Mock.Of<IFormSubmissionProgressRepository>(), _mapper, Mock.Of<IFormConfigurationRepository>(), Mock.Of<IDisplayConditionEvaluator>());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveProgressAsync(Draft("1"), 0));
    }

    [Theory]
    [InlineData(true, null)]   // mine=true (the current web app)
    [InlineData(null, 1)]      // an older client still sending userId=1
    [InlineData(null, 12)]     // another user's id: still only the caller's rows
    public async Task SavedProgressList_IsFilteredByTheCaller(bool? mine, int? sentUserId)
    {
        var service = new Mock<IFormSubmissionProgressService>();
        service.Setup(s => s.GetSummaryByEntityTypeNameAsync("Town", It.IsAny<int?>(), null, null))
            .ReturnsAsync(new List<FormSubmissionProgressSummaryDto>());

        await DraftController(service.Object, ServiceAuthTestHelper.WebUser(7))
            .GetByEntityTypeName("Town", sentUserId, isSummary: true, mine: mine);

        service.Verify(s => s.GetSummaryByEntityTypeNameAsync("Town", 7, null, null), Times.Once);
    }

    [Fact]
    public async Task RelationshipDraftsList_WithoutMine_StillListsEveryOwner()
    {
        var service = new Mock<IFormSubmissionProgressService>();
        service.Setup(s => s.GetSummaryByEntityTypeNameAsync("GateDoor", null, "GateStructureId", "14"))
            .ReturnsAsync(new List<FormSubmissionProgressSummaryDto>());

        await DraftController(service.Object, ServiceAuthTestHelper.WebUser(7))
            .GetByEntityTypeName("GateDoor", null, isSummary: true, propertyName: "GateStructureId", propertyValue: "14");

        service.Verify(s => s.GetSummaryByEntityTypeNameAsync("GateDoor", null, "GateStructureId", "14"), Times.Once);
    }

    [Fact]
    public async Task MineWithoutALoggedInUser_ListsNothing()
    {
        var service = new Mock<IFormSubmissionProgressService>();

        var result = await DraftController(service.Object, ServiceAuthTestHelper.Anonymous())
            .GetByEntityTypeName("Town", null, isSummary: true, mine: true);

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<FormSubmissionProgressSummaryDto>>(Assert.IsType<OkObjectResult>(result).Value));
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetByUser_IgnoresTheQueryAndUsesTheCaller()
    {
        var service = new Mock<IFormSubmissionProgressService>();
        service.Setup(s => s.GetByUserIdAsync(It.IsAny<int>())).ReturnsAsync(new List<FormSubmissionProgressDto>());

        await DraftController(service.Object, ServiceAuthTestHelper.WebUser(7)).GetByUserId(1);

        service.Verify(s => s.GetByUserIdAsync(7), Times.Once);
        service.Verify(s => s.GetByUserIdAsync(1), Times.Never);
    }

    [Fact]
    public async Task Update_KeepsTheOwner()
    {
        var existing = new FormSubmissionProgress { Id = 11, UserId = 7, FormConfigurationId = 3 };
        var repo = new Mock<IFormSubmissionProgressRepository>();
        repo.Setup(r => r.GetByIdAsync(11)).ReturnsAsync(existing);
        var service = new FormSubmissionProgressService(
            repo.Object, _mapper, Mock.Of<IFormConfigurationRepository>(), Mock.Of<IDisplayConditionEvaluator>());

        await service.UpdateProgressAsync(11, Draft("1"));

        Assert.Equal(7, existing.UserId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(99)]
    public async Task WorkflowSession_StartedByAUser_BelongsToThatUser(int sentUserId)
    {
        var service = new Mock<IWorkflowService>();
        service.Setup(s => s.StartSessionAsync(It.IsAny<WorkflowSessionCreateDto>()))
            .ReturnsAsync((WorkflowSessionCreateDto dto) => new WorkflowSessionReadDto { Id = 5, UserId = dto.UserId });

        var result = await WorkflowController(service.Object, ServiceAuthTestHelper.WebUser(7))
            .Create(new WorkflowSessionCreateDto { UserId = sentUserId, EntityTypeName = "Town" });

        Assert.IsType<CreatedAtRouteResult>(result);
        service.Verify(s => s.StartSessionAsync(It.Is<WorkflowSessionCreateDto>(d => d.UserId == 7)), Times.Once);
    }

    [Fact]
    public async Task WorkflowSession_FromTheGameServer_KeepsTheNamedOwner()
    {
        var service = new Mock<IWorkflowService>();
        service.Setup(s => s.StartSessionAsync(It.IsAny<WorkflowSessionCreateDto>()))
            .ReturnsAsync(new WorkflowSessionReadDto { Id = 5, UserId = 4 });

        var result = await WorkflowController(service.Object, ServiceAuthTestHelper.Plugin())
            .Create(new WorkflowSessionCreateDto { UserId = 4 });

        Assert.IsType<CreatedAtRouteResult>(result);
        service.Verify(s => s.StartSessionAsync(It.Is<WorkflowSessionCreateDto>(d => d.UserId == 4)), Times.Once);
    }

    [Fact]
    public async Task WorkflowSession_WithoutAUserOrOwner_IsRefused()
    {
        var service = new Mock<IWorkflowService>();

        var result = await WorkflowController(service.Object, ServiceAuthTestHelper.Plugin())
            .Create(new WorkflowSessionCreateDto { UserId = 0 });

        Assert.IsType<BadRequestObjectResult>(result);
        service.VerifyNoOtherCalls();
    }
}
