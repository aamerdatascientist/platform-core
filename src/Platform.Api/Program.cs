using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.IdentityModel.Tokens;
using Platform.Api.Middleware;
using Platform.Application;
using Platform.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Real local secrets (PostgresConnection, Jwt:Secret) live in this gitignored file
// (see .gitignore's "appsettings.*.local.json" pattern) instead of the tracked
// appsettings.{Environment}.json, which only carries empty placeholders now. AddJsonFile
// alone would append this after everything CreateBuilder already added - including user
// secrets, environment variables, and command-line args - so a local file would silently
// win over an env var override, which is a confusing debugging trap. Moving it to sit
// right after the tracked appsettings.{Environment}.json instead keeps that same relative
// position: it overrides the checked-in template, but an explicit env var or CLI arg (e.g.
// a CI run) still wins over it, same as always.
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.local.json", optional: true, reloadOnChange: true);
var localSettingsSource = builder.Configuration.Sources[^1];
builder.Configuration.Sources.RemoveAt(builder.Configuration.Sources.Count - 1);
var trackedEnvironmentSettingsIndex = builder.Configuration.Sources.ToList().FindIndex(source =>
    source is JsonConfigurationSource { Path: var path } && path == $"appsettings.{builder.Environment.EnvironmentName}.json");
builder.Configuration.Sources.Insert(trackedEnvironmentSettingsIndex + 1, localSettingsSource);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Platform API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the handler remaps standard short claim names (e.g. "sub") to
        // long ClaimTypes URIs on the way in, which breaks CurrentUserService's lookups
        // for the exact claim types JwtTokenService issues.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    // Tightened to the real frontend origin(s) via configuration before any non-local
    // deployment - AllowAnyOrigin is a local-development convenience only.
    options.AddPolicy("Default", policy =>
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseCors("Default");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }
