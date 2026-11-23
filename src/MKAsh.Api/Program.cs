using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using MKAsh.Api.Auth;
using MKAsh.Api.Data;
using MKAsh.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ---- Database ----
builder.Services.AddDbContext<PmsDbContext>(o =>
    o.UseSqlServer(cfg.GetConnectionString("Pms") ?? throw new InvalidOperationException("ConnectionStrings:Pms is not set.")));

// ---- Sign-in ----
// Production: Microsoft Entra ID (ministry accounts). Development can use the X-Dev-User header instead.
var authMode = cfg["Auth:Mode"] ?? "EntraId";
if (authMode == "Development")
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Auth:Mode=Development is only allowed in the Development environment.");
    builder.Services.AddAuthentication(DevAuthHandler.Scheme)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.Scheme, _ => { });
}
else
{
    builder.Services.AddAuthentication("Bearer").AddMicrosoftIdentityWebApi(cfg.GetSection("AzureAd"));
}
builder.Services.AddAuthorization();

// ---- App services ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<ChangeRequestService>();
builder.Services.AddScoped<Seeder>();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Domain exceptions → HTTP status codes with a short message the web app can show.
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    (ctx.Response.StatusCode, var msg) = ex switch
    {
        ForbiddenException f => (403, f.Message),
        NotFoundException n => (404, n.Message),
        ConflictException c => (409, c.Message),
        InvalidOperationException i when i.Message.Contains("cannot be approved") => (409, i.Message),
        _ => (500, "Something went wrong. The error has been logged."),
    };
    await ctx.Response.WriteAsJsonAsync(new { error = msg });
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("web");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallbackToFile("index.html");

// ---- Database creation and first load ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PmsDbContext>();
    if (cfg.GetValue("Database:MigrateOnStartup", true))
    {
        if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();
    }
    if (cfg.GetValue("Seed:OnStartup", false))
    {
        var dir = cfg["Seed:Directory"] ?? Path.Combine(app.Environment.ContentRootPath, "..", "..", "database", "seed");
        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync(dir);
    }
}

app.Run();

public partial class Program { }
