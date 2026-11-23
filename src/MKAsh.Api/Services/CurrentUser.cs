using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Core.Domain;
using MKAsh.Core.Rules;

namespace MKAsh.Api.Services;

/// <summary>
/// Turns the signed-in Entra ID account into a <see cref="UserContext"/>.
/// The account is matched to a person by email; anyone not on the people list gets 403.
/// </summary>
public class CurrentUser(IHttpContextAccessor http, PmsDbContext db)
{
    private UserContext? _ctx;
    private Person? _person;

    public string? Email
    {
        get
        {
            var u = http.HttpContext?.User;
            return (u?.FindFirstValue("preferred_username") ?? u?.FindFirstValue(ClaimTypes.Email)
                    ?? u?.FindFirstValue(ClaimTypes.Upn) ?? u?.FindFirstValue(ClaimTypes.Name))?.Trim().ToLowerInvariant();
        }
    }

    public async Task<Person?> PersonAsync(CancellationToken ct = default)
    {
        if (_person is not null) return _person;
        var email = Email;
        if (email is null) return null;
        _person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Email == email && p.Active, ct);
        return _person;
    }

    /// <summary>The caller's permissions, or null when they are not a registered user.</summary>
    public async Task<UserContext?> ContextAsync(CancellationToken ct = default)
    {
        if (_ctx is not null) return _ctx;
        var p = await PersonAsync(ct);
        if (p is null) return null;
        var managed = p.Role == Role.ProjectManager
            ? (await db.Projects.Where(x => x.ManagerId == p.Id).Select(x => x.Id).ToListAsync(ct)).ToHashSet()
            : new HashSet<int>();
        _ctx = new UserContext(p.Id, p.Role, p.LeadName, managed);
        return _ctx;
    }

    /// <summary>Like <see cref="ContextAsync"/> but throws 403 for unknown users.</summary>
    public async Task<UserContext> RequireAsync(CancellationToken ct = default) =>
        await ContextAsync(ct) ?? throw new ForbiddenException("Your account is not registered in MKAsh PMS. Ask the PMO to add you.");

    public async Task<UserContext> RequirePageAsync(string page, CancellationToken ct = default)
    {
        var u = await RequireAsync(ct);
        if (!AccessRules.CanOpen(u, page)) throw new ForbiddenException($"Your role cannot open {page}.");
        return u;
    }
}

public class ForbiddenException(string message) : Exception(message);
public class NotFoundException(string message) : Exception(message);
public class ConflictException(string message) : Exception(message);
