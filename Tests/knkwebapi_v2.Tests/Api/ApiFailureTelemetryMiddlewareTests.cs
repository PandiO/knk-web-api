using knkwebapi_v2.Enums;
using knkwebapi_v2.Middleware;
using knkwebapi_v2.Services.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// api.request_failed (IMPLEMENTATION_PLAN.md §4; link 6 acceptance criterion 1): recorded for 5xx
/// and exceptions with the route template, never the raw path, query or exception message; the
/// plugin's correlation id is kept; the response is never changed.
/// </summary>
public class ApiFailureTelemetryMiddlewareTests
{
    private readonly Mock<ITelemetryIngestionService> _telemetry = new();
    private IReadOnlyDictionary<string, object?>? _payload;
    private string? _correlation;
    private int? _userId;

    public ApiFailureTelemetryMiddlewareTests()
    {
        _telemetry.Setup(t => t.RecordApiEvent("api.request_failed", TelemetryOutcome.Failed, "request_failed", It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, object?>>()))
            .Callback((string _, TelemetryOutcome _, string _, int? user, string? correlation, string? _, IReadOnlyDictionary<string, object?> payload) =>
            {
                _userId = user;
                _correlation = correlation;
                _payload = payload;
            })
            .Returns(true);
    }

    private static HttpContext Request(string path = "/api/users/42", string? correlation = null)
    {
        var http = ServiceAuthTestHelper.Plugin(actingUserId: 7);
        http.Request.Method = "PUT";
        http.Request.Path = path;
        http.Request.QueryString = new QueryString("?secret=abc");
        if (correlation != null) http.Request.Headers[ApiFailureTelemetryMiddleware.CorrelationHeader] = correlation;
        http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/users/{id:int}"), 0,
            EndpointMetadataCollection.Empty, "users"));
        return http;
    }

    private Task Run(HttpContext http, RequestDelegate next) =>
        new ApiFailureTelemetryMiddleware(next, NullLogger<ApiFailureTelemetryMiddleware>.Instance).InvokeAsync(http, _telemetry.Object);

    [Fact]
    public async Task ServerErrors_AreRecordedWithTheRouteTemplateOnly()
    {
        var http = Request(correlation: "c-123");

        await Run(http, ctx => { ctx.Response.StatusCode = 503; return Task.CompletedTask; });

        Assert.Equal(503, http.Response.StatusCode);
        Assert.Equal(("PUT", "api/users/{id:int}", 503), (_payload!["method"], _payload["route"], _payload["status"]));
        Assert.False(_payload.ContainsKey("exceptionType"));
        Assert.DoesNotContain(_payload.Values, v => v?.ToString()?.Contains("42") == true || v?.ToString()?.Contains("secret") == true);
        Assert.Equal(("c-123", 7), (_correlation, _userId));
    }

    [Fact]
    public async Task Exceptions_AreRecordedByTypeAndRethrown()
    {
        var http = Request(correlation: "bad header with spaces");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Run(http, _ => throw new InvalidOperationException("contains private data")));

        Assert.Equal(("InvalidOperationException", 500), (_payload!["exceptionType"], _payload["status"]));
        Assert.DoesNotContain(_payload.Values, v => v?.ToString()?.Contains("private") == true);
        Assert.NotEqual("bad header with spaces", _correlation);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(499)]
    public async Task OtherAnswers_RecordNothing(int status)
    {
        await Run(Request(), ctx => { ctx.Response.StatusCode = status; return Task.CompletedTask; });

        _telemetry.VerifyNoOtherCalls();
    }

    [Fact]
    public void UnmatchedRoutes_NeverExposeThePath()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/users/by-name/alice";

        Assert.Equal("unmatched", ApiFailureTelemetryMiddleware.RouteTemplateOf(http));
    }
}
