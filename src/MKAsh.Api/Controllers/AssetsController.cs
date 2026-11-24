using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MKAsh.Api.Data;
using MKAsh.Api.Services;

namespace MKAsh.Api.Controllers;

/// <summary>Uploaded files: proof of completion, charters, closure attachments.</summary>
[ApiController]
public class AssetsController(PmsDbContext db, Account me) : ControllerBase
{
    public const long MaxBytes = 20 * 1024 * 1024;

    /// <summary>Attachments everywhere in Tamkeen: PDF, Word, Excel and images (PNG/JPG).</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx", ".xls", ".xlsx", ".xlsm",
    };

    [HttpPost("api/assets"), RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string? type, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file." });
        if (file.Length > MaxBytes) return StatusCode(413, new { error = "Files must be 20 MB or smaller." });
        if (!Allowed.Contains(Path.GetExtension(file.FileName))) return BadRequest(new { error = "unsupported_type" });
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var a = new Asset
        {
            Id = Guid.NewGuid().ToString("N"), FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(type) ? file.ContentType : type, Size = file.Length,
            Data = ms.ToArray(), UploadedBy = me.Email, UploadedAt = DateTime.UtcNow,
        };
        db.Assets.Add(a);
        await db.SaveChangesAsync(ct);
        return Ok(new { id = a.Id, contentType = a.ContentType, sizeBytes = a.Size });
    }

    [HttpGet("_blob/{id}")]
    public async Task<IActionResult> Download(string id, CancellationToken ct)
    {
        var a = await db.Assets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        // Images and PDFs open in the browser; everything else downloads with its original name.
        var inline = a.ContentType.StartsWith("image/") || a.ContentType == "application/pdf";
        return inline ? File(a.Data, a.ContentType) : File(a.Data, a.ContentType, a.FileName);
    }

    [HttpDelete("api/assets/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var a = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NoContent();
        if (a.UploadedBy != me.Email && !await me.IsAdminAsync(ct)) return StatusCode(403, new { error = "Only the uploader or the PMO can delete this file." });
        db.Assets.Remove(a);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
