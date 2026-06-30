using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Extensions;
using Kp.Ms.Sms.Factories;
using Kp.Ms.Sms.Services;
using Kp.Ms.Sms.Middlewares;
using Microsoft.OpenApi.Models;
using Kp.Ms.Sms.Entities.Entity;
using Kp.Ms.Sms.Providers;
using Common.HttpClientWrapper;
using Configurator.Services.Impl;
using NLog.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetRequiredSection("ConnectionString").Get<string>()!;
builder.AddConfigurationModule(connectionString);

builder.Host.UseNLog();

/*
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration.GetRequiredSection("Authorization:Token").Get<string>()!)) // длинный ключ
        };
    });
*/

builder.Services.AddAuthentication("SimpleToken")
    .AddScheme<AuthenticationSchemeOptions, SimpleTokenAuthenticationHandler>("SimpleToken", null);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = false, // <- отключает проверку exp (иначе 401)
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration.GetRequiredSection("Authorization:Token").Get<string>()!))
        };
    });

builder.Services
    .AddControllers()
    .AddJsonOptions(options => { 
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddSwaggerGen(option =>
{
    option.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter a valid token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "Bearer"
    });
    option.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
    option.EnableAnnotations();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();

builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);
builder.Services.Configure<QueueSettings>(builder.Configuration.GetSection("Settings:QueueSettings"));

builder.Services.AddCors(c => c.AddPolicy("cors", opt =>
{
    opt.AllowAnyHeader();
    opt.AllowAnyMethod();
    opt.WithOrigins(builder.Configuration.GetSection("Cors:Urls").Get<string[]>()!);
}));

builder.Services.AddSingleton<IHttpClientWrapper, HttpClientWrapper>();
builder.Services.AddOpenSearch(builder.Configuration);
builder.Services.AddSingleton<ValidationService>();
builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<SmsService>();
builder.Services.AddSingleton<CallService>();

//
builder.Services.AddSingleton<IAuthorizationHandler, StaticTokenHandler>();
builder.Services.AddHttpContextAccessor(); // обязательно!
//

builder.Services.AddHostedService<TimerService>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/healthz")
    .RequireHost(builder.Configuration.GetSection("Settings:MonitoringHosts").Get<string[]>());

var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
if (!app.Environment.IsProduction())
{
    app.UseSwagger(options => { options.RouteTemplate = "/api/swagger/{documentName}/swagger.json"; });
    app.UseSwaggerUI(options =>
    {
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint(
                $"/api/swagger/{description.GroupName}/swagger.json",
                description.GroupName.ToUpperInvariant()
            );
        }

        options.RoutePrefix = "api/swagger";
    });
}

app.UseCors("cors");

//app.UseMiddleware<AuthorizationMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();