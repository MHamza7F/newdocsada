using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using scada_demo_test.API.Filters;
using scada_demo_test.Web.Services;
using Xunit;

namespace Auth.Security.Tests;

public class AuthorizationBoundaryTests
{
    [Fact]
    public async Task ForgedSuperAdminHeaderDoesNotAuthorizeAnonymousCaller()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Requesting-Role"] = "SuperAdmin";
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(action, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        await new RequireUsersTabAttribute().OnActionExecutionAsync(context, () => throw new Exception("Must not execute"));
        Assert.IsType<UnauthorizedResult>(context.Result);
    }

    [Theory]
    [InlineData("SuperAdmin", false)]
    [InlineData("Operator", true)]
    public async Task AuthenticatedSuperAdminSemanticsArePreserved(string role, bool hardcoded)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role), new Claim("IsSuperAdmin", hardcoded ? "true" : "false")
        }, "Bearer")) };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(action, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var called = false;
        await new RequireUsersTabAttribute().OnActionExecutionAsync(context, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(action, new List<IFilterMetadata>(), new object()));
        });
        Assert.True(called);
    }

    [Fact]
    public async Task AnonymousClientCannotBorrowAnotherUsersTokenOrRefresh()
    {
        using var sessions = new WebAuthSessionStore();
        sessions.Create(Tokens(Guid.NewGuid(), "some-other-users-token"));
        using var services = new ServiceCollection().BuildServiceProvider();
        var calls = 0;
        using var client = new HttpClient(new PermissionForwardingHandler(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        }, services, sessions) { InnerHandler = new Stub(request =>
        {
            calls++;
            Assert.Null(request.Headers.Authorization);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }) });
        using var response = await client.GetAsync("https://unit.test/api/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnauthorizedRefreshUsesSameOriginRotatesSessionAndRetriesOnlyOnce()
    {
        using var sessions = new WebAuthSessionStore();
        var userId = Guid.NewGuid();
        var initial = Tokens(userId, "old-access");
        var id = sessions.Create(initial);
        var principal = Principal(userId, id);
        using var services = new ServiceCollection().BuildServiceProvider();
        var calls = 0;
        HttpRequestMessage? first = null;
        using var client = new HttpClient(new PermissionForwardingHandler(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        }, services, sessions) { InnerHandler = new Stub(request =>
        {
            calls++;
            Assert.Equal("unit.test", request.RequestUri!.Host);
            if (calls == 1)
            {
                first = request;
                Assert.Equal("old-access", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            if (calls == 2)
            {
                Assert.Equal("/api/auth/refresh", request.RequestUri.AbsolutePath);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Tokens(userId, "new-access")) };
            }
            Assert.NotSame(first, request);
            Assert.Equal("new-access", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }) });
        using var response = await client.PostAsJsonAsync("https://unit.test/api/devices", new { name = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(3, calls);
        Assert.Equal("new-access", sessions.Find(principal)!.Tokens.AccessToken);
        Assert.Null(sessions.Find(Principal(Guid.NewGuid(), id)));
        sessions.Remove(principal);
        Assert.Null(sessions.Find(principal));
    }

    private static ClaimsPrincipal Principal(Guid userId, string sessionId) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(WebAuthSessionStore.SessionClaim, sessionId)
    }, "Cookies"));

    [Fact]
    public async Task CurrentUserEndpointReceivesBearerTokenAndCallerCancellationIsPreserved()
    {
        using var sessions = new WebAuthSessionStore();
        var userId = Guid.NewGuid();
        var id = sessions.Create(Tokens(userId, "access"));
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancelled = new CancellationTokenSource();
        using var client = new HttpClient(new PermissionForwardingHandler(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = Principal(userId, id) }
        }, services, sessions) { InnerHandler = new Stub(request =>
        {
            Assert.Equal("access", request.Headers.Authorization?.Parameter);
            cancelled.Cancel();
            throw new OperationCanceledException(cancelled.Token);
        }) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync("https://unit.test/api/auth/me", cancelled.Token));
    }

    private static ScadaDemoTestApiClient.LoginResponse Tokens(Guid userId, string token) =>
        new(userId, "test@example.invalid", "Test", "User", "Operator", false, new(), token, "refresh-" + token,
            DateTime.UtcNow.AddMinutes(5), DateTime.UtcNow.AddHours(1));

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
