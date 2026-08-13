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
/// logo, or a site name written per language, or neither.
///
/// <para>The banner resolves them in that order: the logo wins when one is stored, otherwise
/// the site name for the active language, otherwise the translated application name. So an
/// installation can be renamed without producing artwork, and removing a logo reveals the
/// name underneath rather than emptying the banner.</para>
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
    /// Everything the banner needs: whether a logo is stored, its file name, when it changed
    /// (the client's cache-busting token), and the configured site name in each language.
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
        var siteName = await _mediator.Send(new GetBrandingSiteNameQuery(), cancellationToken);
        return Ok(new
        {
            hasLogo = logo is not null,
            fileName = logo?.FileName,
            updatedAt = logo?.UpdatedAt,
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

    private const int MaxLogoBytes = 1024 * 1024;

    /// <summary>
    /// SVG is deliberately excluded. It is otherwise ideal for a logo, but an SVG can carry
    /// script that executes if the image URL is opened directly, and this endpoint is served
    /// same-origin and anonymously. Supporting it safely needs a restrictive CSP response
    /// header on this action rather than just an extension check.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    private static string ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
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
