using Microsoft.AspNetCore.Authorization;

namespace Kp.Ms.Sms.Middlewares;

public class StaticTokenHandler : AuthorizationHandler<StaticTokenRequirement>
{
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public StaticTokenHandler(IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        StaticTokenRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            return Task.CompletedTask;

        var token = authHeader["Bearer ".Length..].Trim();
        var expectedToken = _config["Authorization:Token"];

        if (token == expectedToken)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}