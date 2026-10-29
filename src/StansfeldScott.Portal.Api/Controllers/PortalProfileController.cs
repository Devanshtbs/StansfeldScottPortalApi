using Microsoft.AspNetCore.Mvc;
using StansfeldScott.Portal.Common.Models;
using StansfeldScott.Portal.Domain.Contracts;
using StansfeldScott.Portal.Infrastructure.ExternalClients;

namespace StansfeldScott.Portal.Api.Controllers;

[ApiController]
[Route("api/profile")]
public sealed class PortalProfileController(DataversePortalProfileService profiles) : ControllerBase
{
    [HttpGet("{contactId:guid}")]
    public async Task<ActionResult<ApiResponse<PortalProfileResult>>> Get(Guid contactId, CancellationToken token)
    {
        var profile = await profiles.GetAsync(contactId, token);
        return profile is null ? NotFound(ApiResponse<object>.Fail("Portal profile was not found."))
            : Ok(ApiResponse<PortalProfileResult>.Ok(profile, "Profile loaded."));
    }

    [HttpPut("{contactId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Update(Guid contactId, [FromBody] UpdatePortalContactRequest request, CancellationToken token)
    {
        await profiles.UpdateAsync(contactId, request, token);
        return Ok(ApiResponse<object>.Ok(new { }, "Profile updated."));
    }

    [HttpPut("{contactId:guid}/account")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateAccount(Guid contactId, [FromBody] UpdatePortalAccountRequest request, CancellationToken token)
    {
        await profiles.UpdateAccountAsync(contactId, request, token);
        return Ok(ApiResponse<object>.Ok(new { }, "Account profile updated."));
    }

    [HttpPost("{contactId:guid}/documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(28_000_000)]
    public async Task<ActionResult<ApiResponse<object>>> UploadDocument(Guid contactId, IFormFile file, CancellationToken token)
    {
        const long maxBytes = 20 * 1024 * 1024;
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Choose a PDF file to upload."));
        if (file.Length > maxBytes)
            return BadRequest(ApiResponse<object>.Fail("The PDF must be 20 MB or smaller."));
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail("Only PDF files can be uploaded."));

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, token);
        var bytes = stream.ToArray();
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return BadRequest(ApiResponse<object>.Fail("The selected file is not a valid PDF."));

        var safeFileName = Path.GetFileName(file.FileName);
        var notificationSent = await profiles.UploadAccountDocumentAsync(contactId, safeFileName, bytes, token);
        var message = notificationSent
            ? "PDF uploaded, Account status updated to Waiting for Approval, and CRM notified."
            : "PDF uploaded and Account status updated to Waiting for Approval, but CRM notification failed. Contact support with the upload time.";
        return Ok(ApiResponse<object>.Ok(new { notificationSent }, message));
    }

    [HttpPost("{contactId:guid}/business-references")]
    public async Task<ActionResult<ApiResponse<PortalContactProfile>>> AddContact(Guid contactId, [FromBody] RelatedContactRequest request, CancellationToken token)
    {
        var profile = await profiles.GetAsync(contactId, token);
        if (profile?.Account is null) return NotFound(ApiResponse<object>.Fail("The portal user is not linked to an Account."));
        var created = await profiles.CreateRelatedAsync(profile.Account.AccountId, contactId, request, token);
        return Ok(ApiResponse<PortalContactProfile>.Ok(created, "Business Reference added."));
    }

    [HttpPut("{contactId:guid}/business-references/{relatedContactId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateContact(Guid contactId, Guid relatedContactId, [FromBody] RelatedContactRequest request, CancellationToken token)
    {
        var profile = await profiles.GetAsync(contactId, token);
        if (profile?.Account is null) return NotFound(ApiResponse<object>.Fail("The portal user is not linked to an Account."));
        await profiles.UpdateRelatedAsync(profile.Account.AccountId, contactId, relatedContactId, request, token);
        return Ok(ApiResponse<object>.Ok(new { }, "Business Reference updated."));
    }

    [HttpDelete("{contactId:guid}/business-references/{relatedContactId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteContact(Guid contactId, Guid relatedContactId, CancellationToken token)
    {
        var profile = await profiles.GetAsync(contactId, token);
        if (profile?.Account is null) return NotFound(ApiResponse<object>.Fail("The user is not linked to an Account."));
        if (relatedContactId == contactId) return BadRequest(ApiResponse<object>.Fail("The portal user's own Contact cannot be deleted as a Business Reference."));
        await profiles.DeleteRelatedAsync(profile.Account.AccountId, relatedContactId, token);
        return Ok(ApiResponse<object>.Ok(new { }, "Business Reference deleted."));
    }
}
