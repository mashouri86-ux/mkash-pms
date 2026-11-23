using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MKAsh.Api.Services;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Controllers;

[ApiController, Authorize, Route("api")]
public class MeController(CurrentUser me, IWebHostEnvironment env, IConfiguration cfg) : ControllerBase
{
    /// <summary>Who am I, and which pages may I open. The web app builds its menu from this.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var p = await me.PersonAsync(ct);
        var u = await me.ContextAsync(ct);
        if (p is null || u is null) return StatusCode(403, new { error = "notRegistered", email = me.Email });
        return Ok(new
        {
            p.Id, p.NameAr, p.NameEn, p.Email, Role = PortfolioService.Camel(p.Role), p.LeadName,
            Pages = AccessRules.Pages.Where(kv => kv.Value.Contains(u.Role)).Select(kv => kv.Key),
            ManagedProjects = u.ManagedProjectIds,
            ShowsOverdue = AccessRules.ShowsOverdue(u),
        });
    }

    /// <summary>The strategy roadmap shown on the opening page for every user.</summary>
    [HttpGet("strategy")]
    public async Task<IActionResult> Strategy(CancellationToken ct)
    {
        await me.RequireAsync(ct);
        var dir = cfg["Seed:Directory"] ?? Path.Combine(env.ContentRootPath, "..", "..", "database", "seed");
        var path = Path.Combine(dir, "strategy.json");
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(Path.GetFullPath(path), "application/json");
    }
}
