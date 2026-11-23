using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MKAsh.Api.Auth;

/// <summary>
/// Test-system sign-in (Auth:Mode = Development): type an email, no password.
/// Program.cs refuses to enable it outside the Development environment.
/// </summary>
public static class DevSignIn
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/signin", (string? returnUrl) => Results.Content(Page(returnUrl), "text/html; charset=utf-8")).AllowAnonymous();

        app.MapPost("/signin", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim().ToLowerInvariant();
            var returnUrl = form["returnUrl"].ToString();
            if (email.Length < 3 || !email.Contains('@')) return Results.Redirect("/signin?error=1");
            var id = new ClaimsIdentity(new[] { new Claim("preferred_username", email), new Claim("name", email) }, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
            return Results.Redirect(Url.IsLocal(returnUrl) ? returnUrl : "/");
        }).AllowAnonymous().DisableAntiforgery();

        app.MapGet("/signout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/signin");
        }).AllowAnonymous();
    }

    private static class Url
    {
        public static bool IsLocal(string? u) => !string.IsNullOrEmpty(u) && u.StartsWith('/') && !u.StartsWith("//") && !u.StartsWith("/\\");
    }

    private static string Page(string? returnUrl) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Sign in · MKAsh PMS</title>
        <style>
          body{margin:0;min-height:100vh;display:grid;place-items:center;background:#0D4261;font-family:Inter,"Segoe UI",system-ui,sans-serif;color:#1d1d1b}
          .c{background:#fff;border-radius:14px;padding:28px 30px;width:min(420px,90vw);box-shadow:0 10px 40px rgba(0,0,0,.25)}
          h1{margin:0;color:#0D4261;font-size:24px} p{color:#6b6a64;font-size:13.5px;line-height:1.5}
          input{width:100%;box-sizing:border-box;border:1px solid #d9d5cb;border-radius:8px;padding:10px 12px;font:inherit;margin:6px 0 14px}
          button{width:100%;background:#8A1538;color:#fff;border:0;border-radius:8px;padding:10px;font:inherit;font-weight:600;cursor:pointer}
          .t{display:inline-block;background:#fff4d6;color:#7a5600;border-radius:99px;padding:2px 10px;font-size:12px;font-weight:600}
        </style></head><body>
        <form class="c" method="post" action="/signin">
          <span class="t">Test system</span>
          <h1 style="margin-top:10px">MKAsh PMS</h1>
          <p>Sign in with your email. On this test system there is no password. The first person to sign in becomes the owner and can switch between users to see each role's view.</p>
          <label for="e" style="font-size:13px;font-weight:600">Email</label>
          <input id="e" name="email" type="email" required autofocus dir="ltr" placeholder="you@example.com">
          <input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl ?? "/")}}">
          <button>Sign in</button>
        </form></body></html>
        """;
}
