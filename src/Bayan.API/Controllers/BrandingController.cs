using Bayan.API.Authorization;
using Bayan.Application.Features.Branding;
using MediatR;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bayan.API.Controllers;

/// <summary>
/// Site branding: what the top banner shows in place of the application name — an uploaded
/// logo, or a site name written per language, or neither — plus the icon browsers show on
/// the tab.
///
/// <para>The banner resolves its three in that order: the logo wins when one is stored,
/// otherwise the site name for the active language, otherwise the translated application name.
/// So an installation can be renamed without producing artwork, and removing a logo reveals
/// the name underneath rather than emptying the banner.</para>
///
/// <para>The tab icon is independent of all three. It is a 16 px square rather than a banner,
/// so it is uploaded separately instead of being scaled down from the logo, and when none is
/// stored the browser falls back to its own default.</para>
/// </summary>
[ApiController]
[Route("api/branding")]
[Authorize]
public class BrandingController : ControllerBase
{
    private readonly IMediator _mediator;

    public BrandingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Serves the logo image, or 404 when none is uploaded (the banner then shows the app name).
    /// </summary>
    /// <remarks>
    /// Anonymous on purpose. The banner renders it through a plain <c>&lt;img src&gt;</c>, and the
    /// browser does not attach the JWT to image requests — the auth header is added by an HTTP
    /// interceptor that img tags never go through, so an authorized endpoint would simply 401.
    /// A logo is public branding, not protected data, and this also lets the login page use it.
    /// </remarks>
    [HttpGet("logo")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLogo(CancellationToken cancellationToken)
    {
        var logo = await _mediator.Send(new GetBrandingLogoQuery(), cancellationToken);
        if (logo is null)
            return NotFound();

        // Clients cache-bust with ?v=<updatedAt>, so the bytes at a given URL never change.
        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        // The content type is derived from a name the admin supplied; stop the browser from
        // sniffing its way to a different (scriptable) interpretation of the bytes.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(logo.Content, ContentTypeFor(logo.FileName));
    }

    /// <summary>
    /// Serves the browser-tab icon, or 404 when none is uploaded — browsers then fall back to
    /// their own default, which is what they showed before this feature existed.
    /// </summary>
    /// <remarks>
    /// Anonymous for the same reason as the logo, and more so: a browser fetches the favicon
    /// for the sign-in page, before anyone has a token to attach.
    /// </remarks>
    [HttpGet("favicon")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFavicon(CancellationToken cancellationToken)
    {
        var favicon = await _mediator.Send(new GetBrandingFaviconQuery(), cancellationToken);
        if (favicon is null)
            return NotFound();

        // Not immutable like the logo. The client points the <link> at this URL before it has
        // signed in, so it has no updatedAt to cache-bust with on that first paint; letting the
        // browser revalidate a file this small is cheaper than serving a stale icon for a year.
        Response.Headers["Cache-Control"] = "public, no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(favicon.Content, ContentTypeFor(favicon.FileName));
    }

    /// <summary>
    /// Everything the shell needs: whether a logo and a tab icon are stored, their file names,
    /// when each changed (the client's cache-busting token), and the site name per language.
    /// </summary>
    /// <remarks>
    /// One call rather than one per asset, because the shell reads all of it on every sign-in
    /// to decide what the banner shows. Both site names are returned regardless of the caller's
    /// language: the branding dialog edits the pair, and the language toggle switches between
    /// them without a refetch.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> GetInfo(CancellationToken cancellationToken)
    {
        var logo = await _mediator.Send(new GetBrandingLogoQuery(), cancellationToken);
        var favicon = await _mediator.Send(new GetBrandingFaviconQuery(), cancellationToken);
        var siteName = await _mediator.Send(new GetBrandingSiteNameQuery(), cancellationToken);
        return Ok(new
        {
            hasLogo = logo is not null,
            fileName = logo?.FileName,
            updatedAt = logo?.UpdatedAt,
            hasFavicon = favicon is not null,
            faviconFileName = favicon?.FileName,
            faviconUpdatedAt = favicon?.UpdatedAt,
            siteNameEn = siteName.En,
            siteNameAr = siteName.Ar
        });
    }

    /// <summary>
    /// Sets the banner's site name in either or both languages. Requires <c>branding.manage</c>.
    /// </summary>
    /// <remarks>
    /// A blank value clears that language, which is a supported state rather than an error:
    /// the banner then falls back to the translated application name. Sending both blank
    /// restores the default name in both languages.
    /// </remarks>
    [HttpPut("site-name")]
    [RequirePermission(Permissions.BrandingManage)]
    public async Task<IActionResult> SetSiteName(
        [FromBody] SetSiteNameRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { message = "No site name was supplied." });

        var en = request.SiteNameEn?.Trim();
        var ar = request.SiteNameAr?.Trim();

        // Checked before storing rather than clipped on display: a name the banner would
        // truncate is a mistake worth reporting, not one worth hiding.
        if (en?.Length > SystemSettingKeys.BrandingSiteNameMaxLength ||
            ar?.Length > SystemSettingKeys.BrandingSiteNameMaxLength)
        {
            return BadRequest(new
            {
                message = $"A site name must be {SystemSettingKeys.BrandingSiteNameMaxLength} characters or fewer."
            });
        }

        await _mediator.Send(new SetBrandingSiteNameCommand(en, ar), cancellationToken);
        return NoContent();
    }

    /// <summary>Body of <see cref="SetSiteName"/>; either member may be blank.</summary>
    public record SetSiteNameRequest(string? SiteNameEn, string? SiteNameAr);

    /// <summary>Uploads (or replaces) the site logo. Requires Admin role.</summary>
    [HttpPost("logo")]
    [RequirePermission(Permissions.BrandingManage)]
    [RequestSizeLimit(MaxLogoBytes + 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });
        if (file.Length > MaxLogoBytes)
            return BadRequest(new { message = "The logo must be 1 MB or smaller." });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return BadRequest(new { message = "The logo must be a PNG, JPG or WebP image." });

        byte[] content;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, cancellationToken);
            content = ms.ToArray();
        }

        // Trusting the extension alone would let any bytes be served back under an image
        // content type, so confirm the file really is the format it claims.
        if (!MatchesImageSignature(content, extension))
            return BadRequest(new { message = "The file is not a valid image, or does not match its extension." });

        await _mediator.Send(
            new SetBrandingLogoCommand(Path.GetFileName(file.FileName), content),
            cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Removes the logo; the banner falls back to the configured site name, or to the
    /// application name when none is set. Requires <c>branding.manage</c>.
    /// </summary>
    [HttpDelete("logo")]
    [RequirePermission(Permissions.BrandingManage)]
    public async Task<IActionResult> DeleteLogo(CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteBrandingLogoCommand(), cancellationToken);
        return NoContent();
    }

    /// <summary>Uploads (or replaces) the browser-tab icon. Requires <c>branding.manage</c>.</summary>
    [HttpPost("favicon")]
    [RequirePermission(Permissions.BrandingManage)]
    [RequestSizeLimit(MaxFaviconBytes + 1024)]
    public async Task<IActionResult> UploadFavicon(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });
        if (file.Length > MaxFaviconBytes)
            return BadRequest(new { message = "The tab icon must be 256 KB or smaller." });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedFaviconExtensions.Contains(extension))
            return BadRequest(new { message = "The tab icon must be a PNG, ICO or WebP image." });

        byte[] content;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, cancellationToken);
            content = ms.ToArray();
        }

        if (!MatchesImageSignature(content, extension))
            return BadRequest(new { message = "The file is not a valid image, or does not match its extension." });

        await _mediator.Send(
            new SetBrandingFaviconCommand(Path.GetFileName(file.FileName), content),
            cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Removes the tab icon; browsers fall back to their own default. Requires
    /// <c>branding.manage</c>.
    /// </summary>
    [HttpDelete("favicon")]
    [RequirePermission(Permissions.BrandingManage)]
    public async Task<IActionResult> DeleteFavicon(CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteBrandingFaviconCommand(), cancellationToken);
        return NoContent();
    }

    private const int MaxLogoBytes = 1024 * 1024;

    /// <summary>
    /// A quarter of the logo's allowance. A favicon is a 16–64 px square that every page load
    /// fetches before anything else is painted, so a megabyte of it would be paid for on the
    /// sign-in page of an air-gapped install with nothing to gain.
    /// </summary>
    private const int MaxFaviconBytes = 256 * 1024;

    /// <summary>
    /// SVG is deliberately excluded. It is otherwise ideal for a logo, but an SVG can carry
    /// script that executes if the image URL is opened directly, and this endpoint is served
    /// same-origin and anonymously. Supporting it safely needs a restrictive CSP response
    /// header on this action rather than just an extension check.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    /// <summary>
    /// ICO instead of JPG. A favicon needs a transparent background to sit on a browser's tab
    /// strip, which JPG cannot carry, while ICO is the format most icon tooling still emits and
    /// the only one that packs several sizes into one file. SVG stays excluded for the reason
    /// above.
    /// </summary>
    private static readonly HashSet<string> AllowedFaviconExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".ico", ".webp" };

    private static string ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };

    private static bool MatchesImageSignature(byte[] content, string extension) =>
        extension switch
        {
            ".png" => StartsWith(content, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ".jpg" or ".jpeg" => StartsWith(content, 0xFF, 0xD8, 0xFF),
            // "RIFF" .... "WEBP" — the 4 size bytes in between are not part of the signature.
            ".webp" => StartsWith(content, 0x52, 0x49, 0x46, 0x46)
                       && content.Length >= 12
                       && content[8] == 0x57 && content[9] == 0x45
                       && content[10] == 0x42 && content[11] == 0x50,
            // Reserved word (0), then type 1 = icon. Type 2 is a cursor, which shares the
            // container and would otherwise pass as an icon.
            ".ico" => StartsWith(content, 0x00, 0x00, 0x01, 0x00),
            _ => false
        };

    private static bool StartsWith(byte[] content, params byte[] signature)
    {
        if (content.Length < signature.Length)
            return false;
        for (var i = 0; i < signature.Length; i++)
        {
            if (content[i] != signature[i])
                return false;
        }
        return true;
    }
}
