using System.Net;

namespace Kp.Ms.Sms.Middlewares;

public class AuthorizationMiddleware
{
    private readonly RequestDelegate _next;

    public AuthorizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        try
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

            if (string.IsNullOrEmpty(authHeader))
            {
                await RespondWithErrorAsync(context, HttpStatusCode.Unauthorized, "Authorization header is not set.");
                return;
            }

            if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                await RespondWithErrorAsync(context, HttpStatusCode.BadRequest, "Authorization token format is wrong. It must start with 'Bearer '.");
                return;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();

            if (string.IsNullOrEmpty(token))
            {
                await RespondWithErrorAsync(context, HttpStatusCode.Unauthorized, "Authorization token is empty.");
                return;
            }

            var validToken = ValidateToken(token, configuration);
            if (!validToken)
            {
                await RespondWithErrorAsync(context, HttpStatusCode.Forbidden, "Authorization token is wrong.");
                return;
            }

            await _next(context);
        }
        catch (Exception ex)
        {
            await RespondWithErrorAsync(context, HttpStatusCode.InternalServerError, "An unexpected error occurred during authorization.");
        }
    }

    private bool ValidateToken(string token, IConfiguration configuration)
    {
        var validToken = configuration["Authorization:Token"];
        return token == validToken;
    }

    private async Task RespondWithErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            code = context.Response.StatusCode,
            message
        };

        await context.Response.WriteAsJsonAsync(errorResponse);
    }
}
