using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Kp.Ms.Sms.Middlewares;

public class SimpleTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;

    public SimpleTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IConfiguration configuration)
        : base(options, logger, encoder, clock)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization Header"));

        var authHeader = Request.Headers["Authorization"].ToString();

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));

        var token = authHeader["Bearer ".Length..].Trim();
        var expectedToken = _configuration["Authorization:Token"];

        if (token != expectedToken)
            return Task.FromResult(AuthenticateResult.Fail("Invalid Token"));

        var claims = new[] { new Claim(ClaimTypes.Name, "StaticTokenUser") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}