using System.Threading.RateLimiting;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ArmoryWiki.Api.Features.Admin;

public sealed record LoginRequest(string Email, string Password);

public sealed record AdminUserDto(string Email, IReadOnlyList<string> Roles);

public sealed record AntiforgeryTokenDto(string Token, string HeaderName);

/// <summary>
/// Cookie login for admins (no public registration), CSRF protection for admin writes, and the admin account
/// created from configuration. Terms: GLOSSARY.md *admin*.
/// </summary>
public static class AdminAuth
{
    public const string Role = "Admin";
    public const string Policy = "Admin";
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";
    private const string LoginRateLimit = "login";

    public static void AddAdminAuth(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddIdentityCore<IdentityUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Password.RequiredLength = 12;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ArmoryWikiDbContext>()
            .AddSignInManager();

        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "armorywiki.admin";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.SlidingExpiration = true;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            // An API answers 401/403; it never redirects to a login page.
            o.Events.OnRedirectToLogin = ctx => SetStatus(ctx.Response, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = ctx => SetStatus(ctx.Response, StatusCodes.Status403Forbidden);
        });

        builder.Services.AddAuthorizationBuilder().AddPolicy(Policy, p => p.RequireRole(Role));

        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = AntiforgeryHeader;
            o.Cookie.Name = "armorywiki.antiforgery";
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });

        var perMinute = builder.Configuration.GetValue("RateLimits:LoginPerMinute", 10);
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
        });
    }

    private static Task SetStatus(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates the Admin role and the admin account from Admin:Email / Admin:Password when missing, in every
    /// environment (user secrets in Development, .env in production). The password is only used on creation.
    /// </summary>
    public static async Task EnsureAdminAccountAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminAuth));

        if (!await roles.RoleExistsAsync(Role))
        {
            Check(await roles.CreateAsync(new IdentityRole(Role)));
        }

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Admin:Email / Admin:Password are not configured; no admin account was created.");
            return;
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Check(await users.CreateAsync(user, password));
            logger.LogInformation("Created admin account {Email}.", email);
        }

        if (!await users.IsInRoleAsync(user, Role))
        {
            Check(await users.AddToRoleAsync(user, Role));
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }

    public static void MapAdminAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/admin/auth").WithTags("Admin: auth");
        // Login is exempt from the antiforgery check: there is no session yet, and the cookie is SameSite=Strict.
        auth.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimit)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/logout", Logout).RequireAuthorization(Policy).RequireAntiforgeryHeader();
        auth.MapGet("/me", Me).RequireAuthorization(Policy);
        auth.MapGet("/antiforgery", Antiforgery).RequireAuthorization(Policy);
    }

    /// <summary>
    /// One answer for every failure (unknown email, wrong password, locked account) and a password hash computed
    /// either way, so neither the message nor the timing reveals which emails have accounts.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Login(
        LoginRequest request, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn)
    {
        var user = await users.FindByEmailAsync(request.Email ?? "");
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(TimingDummy, TimingDummyHash.Value, request.Password ?? "");
            return InvalidLogin();
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password ?? "", isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded ? TypedResults.NoContent() : InvalidLogin();
    }

    private static ProblemHttpResult InvalidLogin() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Login failed",
            detail: "Wrong email or password, or too many failed attempts (wait 15 minutes).");

    private static readonly IdentityUser TimingDummy = new();
    private static readonly Lazy<string> TimingDummyHash =
        new(() => new PasswordHasher<IdentityUser>().HashPassword(TimingDummy, Guid.NewGuid().ToString()));

    private static async Task<NoContent> Logout(SignInManager<IdentityUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<AdminUserDto>> Me(HttpContext http, UserManager<IdentityUser> users)
    {
        var user = (await users.GetUserAsync(http.User))!;
        return TypedResults.Ok(new AdminUserDto(user.Email!, [.. await users.GetRolesAsync(user)]));
    }

    /// <summary>The token is bound to the logged-in admin: fetch it after login and send it in <see cref="AntiforgeryHeader"/>.</summary>
    private static Ok<AntiforgeryTokenDto> Antiforgery(HttpContext http, IAntiforgery antiforgery) =>
        TypedResults.Ok(new AntiforgeryTokenDto(antiforgery.GetAndStoreTokens(http).RequestToken!, AntiforgeryHeader));

    /// <summary>Unsafe requests (anything but GET/HEAD/OPTIONS) must carry a valid antiforgery token header.</summary>
    public static TBuilder RequireAntiforgeryHeader<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var safe = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method);
            if (!safe && !await http.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(http))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Missing or invalid antiforgery token",
                    detail: $"Send the token from GET /api/admin/auth/antiforgery in the {AntiforgeryHeader} header.");
            }

            return await next(context);
        });
}
