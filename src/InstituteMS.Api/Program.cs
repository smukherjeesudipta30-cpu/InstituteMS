using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using InstituteMS.Api.Filters;
using InstituteMS.Api.Hubs;
using InstituteMS.Api.Middleware;
using InstituteMS.Application.Auth;
using InstituteMS.Application.Chat;
using InstituteMS.Application.Common;
using InstituteMS.Infrastructure;
using InstituteMS.Infrastructure.Persistence;
using InstituteMS.Infrastructure.Security;
using InstituteMS.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
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
builder.Services.AddHostedService<TeacherExamDueReminderService>();
builder.Services.AddHostedService<BirthdayNotificationService>();

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
                    "You do not have permission to view this page."),

            /* SignalR's browser client cannot set an Authorization header on the
               WebSocket upgrade request itself, so it puts the access token on the
               query string instead (?access_token=...) and this reads it from
               there — scoped to the chat hub's own path only, so no other request
               gains a second way to carry its token. */
            OnMessageReceived = ctx =>
            {
                if (ctx.HttpContext.Request.Path.StartsWithSegments("/hubs/chat"))
                {
                    var token = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token)) ctx.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    InstituteMS.Api.Security.SuperAdminAuthorizationHandler>();

// ------------------------------------------------------------------- chat
builder.Services.AddSignalR();
builder.Services.AddScoped<IChatNotifier, ChatNotifier>();

// Singleton: presence is one shared in-memory count per process, not a per-request thing.
builder.Services.AddSingleton<IChatPresenceTracker, ChatPresenceTracker>();

// ------------------------------------------------------------------- MVC
builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();

        // Global, so a new controller is covered the moment it is written rather
        // than when someone remembers to annotate it.
        options.Filters.Add<PasswordResetFilter>();
    })
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
// Holds files the website generates for a few minutes so phone browsers can
// download them from an https address (see DownloadsController).
builder.Services.AddMemoryCache();

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

/* Angular's build fingerprints every JS/CSS filename with a content hash
   (main-XXXXXXXX.js) - a changed file always gets a new name, so those are
   safe to cache forever. index.html is the one file that keeps its name
   across every deploy while what it points at changes underneath it, so it
   must never be cached at all: without this, a browser can go on serving a
   stale index.html (and therefore stale bundle references) for as long as it
   likes after a redeploy, with no way for the app to tell it otherwise -
   exactly how a shipped feature can be invisible in one browser while working
   in another until a hard refresh. Neither ever had an explicit Cache-Control
   header before, which just left the browser's own heuristics
   (Last-Modified-based) to decide, unpredictably. Shared between the static
   file middleware below and the SPA fallback further down, so index.html gets
   the same "no-cache" whichever of the two actually serves it. */
var spaStaticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var isFingerprinted = System.Text.RegularExpressions.Regex.IsMatch(
            ctx.File.Name, @"-[A-Za-z0-9_]{8,}\.(js|css)$");

        ctx.Context.Response.Headers.CacheControl = isFingerprinted
            ? "public,max-age=31536000,immutable"
            : "no-cache";
    }
};

/* Serves the built Angular application from wwwroot when it is present.
   ---------------------------------------------------------------------
   In production the Angular build sets apiBaseUrl to "/api/v1" - a relative
   path - so the browser expects the site and the API on the SAME origin.
   Without this the published site would need a reverse proxy (IIS ARR) in
   front of two applications just to put them back on one host.

   Guarded on the folder existing, so development is untouched: there is no
   wwwroot in the repository and `ng serve` keeps proxying as before. */
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    /* The ACME http-01 challenge, served for Let's Encrypt at issue and at every
       renewal thereafter. It needs its own middleware because the challenge token
       is an extensionless file with no registered MIME type, and the ordinary
       static-file middleware refuses to serve those. Scoped to /.well-known so
       ServeUnknownFileTypes cannot loosen anything else, and paired with the
       ".well-known" exclusion in the SPA fallback below - without that exclusion
       routing claims the request first and this never runs. */
    var wellKnown = Path.Combine(app.Environment.WebRootPath, ".well-known");
    Directory.CreateDirectory(wellKnown);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wellKnown),
        RequestPath = "/.well-known",
        ServeUnknownFileTypes = true,
        DefaultContentType = "text/plain"
    });

    app.UseDefaultFiles();
    app.UseStaticFiles(spaStaticFileOptions);
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHealthChecks("/health").AllowAnonymous();

/* Angular routes are client-side: a browser asked for /students directly - a
   refresh, or a pasted link - must still be handed index.html rather than a
   404. Registered after MapControllers so it can never shadow an API route,
   and excluded for /api and /health so a genuine 404 there stays a 404
   instead of returning HTML to something expecting JSON. */
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    /* Two constraints, both load-bearing:

       "nonfile" is the one MapFallbackToFile applies by default, and it has to be
       put back by hand once a custom pattern replaces the default. WebApplication
       inserts UseRouting at the TOP of the pipeline, so an endpoint is selected
       before UseStaticFiles runs - and StaticFileMiddleware deliberately stands
       down on any request that already has one. Without "nonfile" this fallback
       matches /main.js and /styles.css as well, every asset is answered with
       index.html at 200, and the application renders a blank page.

       The regex excludes /api and /health. Without it an unknown API route
       would return index.html with status 200, so a caller expecting JSON would
       get a page of HTML and a "success" - the worst kind of 404.

       It also excludes /.well-known, which carries the Let's Encrypt challenge.
       Those tokens are extensionless, so "nonfile" does not cover them: without
       this the certificate renewal is answered with index.html and fails.

       And "r/", the receipt download links sent to students on WhatsApp. The
       trailing slash is load-bearing: a bare "r" would also exclude /reports and
       /receptionists, which are Angular routes and must keep falling through. */
    app.MapFallbackToFile(
        "{*path:nonfile:regex(^(?!api|health|[.]well-known|r/).*$)}", "index.html", spaStaticFileOptions);
}

// Creates the first Admin account when dbo.Users is empty.
await DbSeeder.SeedAsync(app.Services);

// Encrypts any student portal password still in clear. Does nothing while
// StudentPasswords:EncryptionKey is empty, which it is until the legacy
// application stops reading that column.
if (InstituteMS.Infrastructure.Security.StudentPasswordCipher.Enabled)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InstituteMS.Infrastructure.Persistence.AppDbContext>();
    var count = await InstituteMS.Infrastructure.Security.StudentPasswordCipher.EncryptStoredAsync(db);
    if (count > 0) app.Logger.LogInformation("Encrypted {Count} stored student passwords.", count);
}

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
