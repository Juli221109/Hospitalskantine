using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Hospitalskantine;

var dataDirectory = Environment.GetEnvironmentVariable("KANTINE_DATA_DIR");
if (string.IsNullOrWhiteSpace(dataDirectory))
{
    dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Hospitalskantine");
}

var files = new FilService(dataDirectory);
var canteen = new KantineService(files);

if (args.Length > 0 && args[0] == "--create-user")
{
    if (args.Length != 3 || args[0] != "--create-user")
    {
        Console.Error.WriteLine("Brug: dotnet run -- --create-user <brugernavn> <patient|canteen>");
        return;
    }

    try
    {
        var role = args[2] switch
        {
            "patient" => "patient",
            "canteen" => "canteen",
            _ => throw new ArgumentException("Rollen skal være patient eller canteen.")
        };
        var password = ReadPassword(role == "patient"
            ? "Patientens adgangskode (præcis fire tal): "
            : "Kantinebrugerens adgangskode (mindst 12 tegn): ");
        Console.WriteLine($"Bruger oprettet. Login: {args[1]}, rolle: {role}, ID: {canteen.OpretBruger(args[1], password, role)}");
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine(exception.Message);
        Environment.ExitCode = 1;
    }
    return;
}

var builder = WebApplication.CreateBuilder(args);
if (string.Equals(
        Environment.GetEnvironmentVariable("KANTINE_DEMO_MODE"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    canteen.SikreDemoKonti();
}

builder.Services.AddSingleton(files);
builder.Services.AddSingleton(canteen);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Hospitalskantine.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/session", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated != true)
    {
        return Results.Json(new { user = (object?)null });
    }

    return Results.Json(new
    {
        user = new
        {
            username = user.Identity.Name,
            role = user.FindFirstValue(ClaimTypes.Role),
            patientId = user.FindFirstValue("patientId")
        }
    });
});

app.MapPost("/api/login", async (HttpContext context, KantineService service) =>
{
    if (!IsSameOrigin(context))
    {
        return Results.Json(new { error = "Anmodningen kunne ikke godkendes." }, statusCode: 403);
    }

    LoginRequest? request;
    try
    {
        request = await context.Request.ReadFromJsonAsync<LoginRequest>();
    }
    catch (JsonException)
    {
        return Results.Json(new { error = "Loginoplysningerne har et ugyldigt format." }, statusCode: 400);
    }

    if (request is null || string.IsNullOrWhiteSpace(request.Brugernavn) ||
        request.Adgangskode is null)
    {
        return Results.Json(new { error = "Indtast brugernavn og adgangskode." }, statusCode: 400);
    }

    var user = service.LogInd(request.Brugernavn, request.Adgangskode);
    if (user is null)
    {
        return Results.Json(new { error = "Forkert brugernavn eller adgangskode." }, statusCode: 401);
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.Brugernavn),
        new(ClaimTypes.Role, user.Rolle)
    };
    if (user.PatientId is not null)
    {
        claims.Add(new Claim("patientId", user.PatientId));
    }

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Json(new
    {
        user = new { username = user.Brugernavn, role = user.Rolle, patientId = user.PatientId }
    });
});

app.MapPost("/api/logout", async (HttpContext context) =>
{
    if (!IsSameOrigin(context))
    {
        return Results.Json(new { error = "Anmodningen kunne ikke godkendes." }, statusCode: 403);
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Json(new { ok = true });
}).RequireAuthorization();

app.MapGet("/api/choices", (ClaimsPrincipal user, KantineService service) =>
{
    var patientId = user.FindFirstValue("patientId");
    return patientId is null
        ? Results.Forbid()
        : Results.Json(new { days = service.HentValg(patientId, DateOnly.FromDateTime(DateTime.Today)) });
}).RequireAuthorization(new AuthorizeAttribute { Roles = "patient" });

app.MapPost("/api/choices", async (HttpContext context, ClaimsPrincipal user, KantineService service) =>
{
    if (!IsSameOrigin(context))
    {
        return Results.Json(new { error = "Anmodningen kunne ikke godkendes." }, statusCode: 403);
    }

    var patientId = user.FindFirstValue("patientId");
    if (patientId is null)
    {
        return Results.Forbid();
    }

    ChoicesRequest? request;
    try
    {
        request = await context.Request.ReadFromJsonAsync<ChoicesRequest>();
    }
    catch (JsonException)
    {
        return Results.Json(new { error = "Valgene har et ugyldigt format." }, statusCode: 400);
    }

    if (request?.Days is null)
    {
        return Results.Json(new { error = "Der mangler måltidsvalg." }, statusCode: 400);
    }

    try
    {
        service.GemValg(patientId, request.Days, DateOnly.FromDateTime(DateTime.Today));
        return Results.Json(new { ok = true });
    }
    catch (ArgumentException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: 400);
    }
}).RequireAuthorization(new AuthorizeAttribute { Roles = "patient" });

app.MapGet("/api/counts", (KantineService service) =>
    Results.Json(new { days = service.HentOptaellinger(DateOnly.FromDateTime(DateTime.Today)) }))
    .RequireAuthorization(new AuthorizeAttribute { Roles = "canteen" });

app.Run();

static bool IsSameOrigin(HttpContext context)
{
    var origin = context.Request.Headers.Origin.ToString();
    return Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin) &&
        string.Equals(parsedOrigin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(parsedOrigin.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
}

static string ReadPassword(string prompt)
{
    Console.Write(prompt);
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? throw new InvalidOperationException("Der blev ikke indtastet en adgangskode.");
    }

    var password = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return password.ToString();
        }

        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
        }
    }
}

public sealed record LoginRequest(string Brugernavn, string Adgangskode);
public sealed record ChoicesRequest(List<Valgdag> Days);
