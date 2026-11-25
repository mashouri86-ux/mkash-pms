using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using MKAsh.Api.Auth;
using MKAsh.Api.Controllers;
using MKAsh.Api.Data;
using MKAsh.Api.Services;

// The web app (the dashboard screens) lives in /web. Run from the repository it is served from there;
// a published build carries it in wwwroot.
// Look upwards from where the server runs for the repository's web folder; otherwise use wwwroot.
static string? FindWebRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var web = Path.Combine(dir.FullName, "web");
            if (File.Exists(Path.Combine(web, "index.html")) && Directory.Exists(Path.Combine(web, "system"))) return web;
            var root = Path.Combine(dir.FullName, "wwwroot");
            if (File.Exists(Path.Combine(root, "index.html"))) return root;
        }
    }
    return null;
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = FindWebRoot(),
});
var cfg = builder.Configuration;

// ---- Database: SQL Server in production; "Sqlite" (a single file) for quick tests such as Codespaces ----
var connection = cfg.GetConnectionString("Pms") ?? throw new InvalidOperationException("ConnectionStrings:Pms is not set.");
var useSqlite = string.Equals(cfg["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase);
builder.Services.AddDbContext<PmsDbContext>(o =>
{
    if (useSqlite) o.UseSqlite(connection);
    else o.UseSqlServer(connection);
});

// ---- Sign-in: a cookie for the whole site, so pages, data and files all use the same session ----
var devAuth = cfg["Auth:Mode"] == "Development";
if (devAuth)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Auth:Mode=Development is only allowed in the Development environment.");
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(o =>
        {
            o.Cookie.Name = "mkash.session";
            o.LoginPath = "/signin";
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = 401;
                else ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };
        });
}
else
{
    // Microsoft Entra ID (ministry accounts), OpenID Connect + cookie.
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(o =>
        {
            cfg.Bind("AzureAd", o);
            // Data calls from the page get 401 (the page then reloads through sign-in) instead of a redirect.
            o.Events.OnRedirectToIdentityProvider = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.HandleResponse();
                }
                return Task.CompletedTask;
            };
        });
    builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o => o.Cookie.Name = "mkash.session");
}
// Everything requires sign-in unless marked otherwise (pages, data files, uploads).
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Account>();
builder.Services.AddScoped<Seeder>();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = AssetsController.MaxBytes + 1024 * 1024);
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(ex, "Unhandled error");
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "Something went wrong. The error has been logged." });
}));

app.UseAuthentication();
app.UseAuthorization();
// Only people added to Tamkeen get data. Anyone else who signs in sees the "not registered" screen.
app.Use(async (ctx, next) =>
{
    var p = ctx.Request.Path;
    var guarded = (p.StartsWithSegments("/api") && !p.StartsWithSegments("/api/health") && !p.StartsWithSegments("/api/me"))
                  || p.StartsWithSegments("/_blob")
                  || (p.StartsWithSegments("/data") && !p.StartsWithSegments("/data/brand") && !p.StartsWithSegments("/data/fonts"));
    if (guarded && ctx.User.Identity?.IsAuthenticated == true
        && !await ctx.RequestServices.GetRequiredService<MKAsh.Api.Services.Account>().IsRegisteredAsync(ctx.RequestAborted))
    {
        if (p.StartsWithSegments("/api/docs")) app.Logger.LogWarning("Sign-in by an email that isn't registered: {Email}", ctx.RequestServices.GetRequiredService<MKAsh.Api.Services.Account>().Email);
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { error = "not-registered", message = "This email isn't registered in Tamkeen. Ask the PMO to add you." });
        return;
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // The dashboard's data files are small and change with releases: always revalidate.
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",
});
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
// Logos are public so the sign-in page can show them.
app.MapGet("/data/brand/{file}", (string file, IWebHostEnvironment env) =>
{
    if (file.Contains("..") || file.Contains('/') || file.Contains('\\') || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        return Results.NotFound();
    var f = env.WebRootFileProvider.GetFileInfo("data/brand/" + file);
    return f.Exists ? Results.File(f.PhysicalPath!, file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png") : Results.NotFound();
}).AllowAnonymous();
if (devAuth) DevSignIn.Map(app);
else app.MapGet("/signout", () => Results.SignOut(null, new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme }));

// ---- Database creation and first load ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PmsDbContext>();
    if (cfg.GetValue("Database:MigrateOnStartup", true))
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!useSqlite && db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
                else await db.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex) when (attempt < 20 && ex is Microsoft.Data.SqlClient.SqlException or Microsoft.Data.Sqlite.SqliteException)
            {
                app.Logger.LogWarning("Database not reachable yet (attempt {Attempt}); retrying in 3 s.", attempt);
                await Task.Delay(3000);
            }
        }
    }
    if (cfg.GetValue("Seed:OnStartup", false))
    {
        var dir = cfg["Seed:Directory"] ?? Path.Combine(app.Environment.ContentRootPath, "..", "..", "database", "seed", "docs");
        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync(dir);
    }
}

app.Run();

public partial class Program { }
