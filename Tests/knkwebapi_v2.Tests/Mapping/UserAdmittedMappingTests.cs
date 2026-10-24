using AutoMapper;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Tests.Mapping;

/// <summary>KNG-214: UserDto.admitted reads AdmittedAt for the staff buttons, and the generic edit
/// (UserDto → User) can never change it.</summary>
public class UserAdmittedMappingTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<UserMappingProfile>()).CreateMapper();

    [Fact]
    public void Admitted_FollowsAdmittedAt()
    {
        Assert.True(_mapper.Map<UserDto>(new User { Id = 1, Username = "a", AdmittedAt = DateTime.UtcNow }).Admitted);
        Assert.False(_mapper.Map<UserDto>(new User { Id = 2, Username = "b" }).Admitted);
    }

    [Fact]
    public void TheGenericEdit_LeavesAdmittedAtAlone()
    {
        var admittedAt = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var user = new User { Id = 1, Username = "a", AdmittedAt = admittedAt };

        _mapper.Map(new UserDto { Id = 1, Username = "a", Admitted = false }, user);

        Assert.Equal(admittedAt, user.AdmittedAt);
    }
}
