using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using InstituteMS.Api.Filters;
using InstituteMS.Api.Middleware;
using InstituteMS.Application.Auth;
using InstituteMS.Application.Common;
using InstituteMS.Infrastructure;
using InstituteMS.Infrastructure.Persistence;
using InstituteMS.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- logging
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ----------------------------------------------------------- infrastructure
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

// ------------------------------------------------------------------- auth
var jwt = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
          ?? throw new InvalidOperationException("The Jwt configuration section is missing.");

if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be set and at least 32 characters long. " +
        "In development it lives in appsettings.Development.json; in production supply it " +
        "via the JWT__KEY environment variable or Key Vault.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtTokenService.SigningKey(jwt.Key),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Return the standard envelope for 401/403 instead of an empty body,
        // so the Angular error interceptor has something to show.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                await WriteEnvelopeAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized,
                    "Your session has expired. Please sign in again.");
            },
            OnForbidden = async ctx =>
                await WriteEnvelopeAsync(ctx.HttpContext, StatusCodes.Status403Forbidden,
                    "You do not have permission to view this page.")
        };
    });

builder.Services.AddAuthorization();

// ------------------------------------------------------------------- MVC
builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Model-binding failures (bad JSON, wrong types) get the same envelope.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .SelectMany(kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage))
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();

        var body = ApiResponse<object?>.Fail("The request could not be read.", errors);
        body.TraceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

        return new BadRequestObjectResult(body);
    };
});

// ------------------------------------------------------------------- CORS
const string CorsPolicy = "AngularClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:4200"];

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
{
    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();

    if (builder.Environment.IsDevelopment())
    {
        // In development, accept any loopback origin whatever the port.
        // "localhost", "127.0.0.1" and "[::1]" are three DIFFERENT origins to a
        // browser, and the Angular dev server picks another port when 4200 is taken.
        // A fixed list therefore produces a CORS rejection, which the browser
        // reports to the client as a bare network error — surfacing as the
        // misleading "cannot reach the server" even though the API is running.
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));
    }
    else
    {
        policy.WithOrigins(allowedOrigins);
    }
}));

// ----------------------------------------------------------- rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Brute-force guard on the login endpoint.
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Spam guard on the public contact form.
    options.AddPolicy("contact", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        }));

    options.OnRejected = async (context, ct) =>
        await WriteEnvelopeAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
            "Too many requests. Please wait a moment and try again.", ct);
});

// ---------------------------------------------------------------- swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "InstituteMS API",
        Version = "v1",
        Description = "Avijan institute management API. This spec is the contract the Angular client " +
                      "and the Android APK are generated from."
    });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken returned by POST /api/v1/auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });

    var xml = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// ------------------------------------------------------------- pipeline
app.UseGlobalExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "InstituteMS API v1");
        options.DocumentTitle = "InstituteMS API";
    });
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

// Creates the first Admin account when dbo.Users is empty.
await DbSeeder.SeedAsync(app.Services);

app.Run();

static async Task WriteEnvelopeAsync(
    HttpContext context, int statusCode, string message, CancellationToken ct = default)
{
    if (context.Response.HasStarted) return;

    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/json";

    var body = new ApiResponse<object?>
    {
        Success = false,
        Message = message,
        TraceId = Activity.Current?.Id ?? context.TraceIdentifier
    };

    await context.Response.WriteAsync(
        JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }), ct);
}

/// <summary>Exposed so integration tests can spin the API up with WebApplicationFactory.</summary>
public partial class Program;
