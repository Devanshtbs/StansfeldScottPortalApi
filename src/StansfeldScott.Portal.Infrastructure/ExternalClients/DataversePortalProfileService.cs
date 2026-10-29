using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using StansfeldScott.Portal.Common.Exceptions;
using StansfeldScott.Portal.Domain.Contracts;

namespace StansfeldScott.Portal.Infrastructure.ExternalClients;

/// <summary>Reads and updates a portal Contact, its Account, and Business References linked to that Account.</summary>
public sealed class DataversePortalProfileService(
    IConfiguration configuration,
    ILogger<DataversePortalProfileService> logger,
    IHttpClientFactory httpClientFactory)
{
    public Task<PortalProfileResult?> GetAsync(Guid contactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = Connect();
        try
        {
            var contact = service.Retrieve("contact", contactId,
                new ColumnSet("firstname", "lastname", "fullname", "emailaddress1", "telephone1", "jobtitle", "parentcustomerid"));
            var user = ToContact(contact);
            var accountRef = contact.GetAttributeValue<EntityReference>("parentcustomerid");
            if (accountRef?.LogicalName != "account")
                return Task.FromResult<PortalProfileResult?>(new PortalProfileResult(user, null, []));

            var account = service.Retrieve("account", accountRef.Id,
                new ColumnSet("name", "accountnumber", "telephone1", "websiteurl", "address1_city", "address1_stateorprovince", "address1_country", "address1_postalcode"));
            var accountProfile = new PortalAccountProfile(
                account.Id, Text(account, "name"), Text(account, "accountnumber"), Text(account, "telephone1"),
                Text(account, "websiteurl"), Text(account, "address1_city"), Text(account, "address1_stateorprovince"),
                Text(account, "address1_country"), Text(account, "address1_postalcode"));

            var query = new QueryExpression("ssf_businessreferences")
            {
                ColumnSet = new ColumnSet("ssf_name", "ssf_email", "ssf_phone", "ssf_jobtitle", "ssf_country", "ssf_distributor"),
                Criteria = new FilterExpression(LogicalOperator.And)
            };
            query.Criteria.AddCondition("ssf_distributor", ConditionOperator.Equal, account.Id);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            Entity[] businessReferences;
            try
            {
                businessReferences = service.RetrieveMultiple(query).Entities.ToArray();
            }
            catch (Exception ex)
            {
                // A missing/misconfigured Business Reference table should not take down
                // the user's working profile, Account editor, or document upload.
                logger.LogError(ex, "Dataverse could not load Business References for Account {AccountId}.", account.Id);
                businessReferences = [];
            }
            var otherContacts = businessReferences.Select(ToBusinessReference).ToArray();
            return Task.FromResult<PortalProfileResult?>(new PortalProfileResult(user, accountProfile, otherContacts));
        }
        catch (Exception ex) when (ex is not CrmIntegrationException)
        {
            throw Failure("Dataverse could not load the portal profile.", ex);
        }
    }

    public Task UpdateAsync(Guid contactId, UpdatePortalContactRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = Connect();
        try
        {
            service.Update(new Entity("contact", contactId)
            {
                ["firstname"] = request.FirstName.Trim(), ["lastname"] = request.LastName.Trim(),
                ["emailaddress1"] = request.Email.Trim(), ["telephone1"] = request.Phone?.Trim(),
                ["jobtitle"] = request.JobTitle?.Trim()
            });
            return Task.CompletedTask;
        }
        catch (Exception ex) { throw Failure("Dataverse could not update this profile.", ex); }
    }

    public Task UpdateAccountAsync(Guid contactId, UpdatePortalAccountRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = Connect();
        try
        {
            var accountId = GetLinkedAccountId(service, contactId);
            service.Update(new Entity("account", accountId)
            {
                ["name"] = request.Name.Trim(), ["accountnumber"] = request.AccountNumber?.Trim(),
                ["telephone1"] = request.Phone?.Trim(), ["websiteurl"] = request.Website?.Trim(),
                ["address1_city"] = request.City?.Trim(), ["address1_stateorprovince"] = request.StateOrProvince?.Trim(),
                ["address1_country"] = request.Country?.Trim(), ["address1_postalcode"] = request.PostalCode?.Trim()
            });
            return Task.CompletedTask;
        }
        catch (Exception ex) { throw Failure("Dataverse could not update this account.", ex); }
    }

    public async Task<bool> UploadAccountDocumentAsync(Guid contactId, string fileName, byte[] pdfBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = Connect();
        try
        {
            var accountId = GetLinkedAccountId(service, contactId);
            const int waitingForApprovalStatusValue = 924060003;
            var statusColumn = FindInvitationStatusColumn(service, waitingForApprovalStatusValue, "Waiting for Approval");
            var note = new Entity("annotation")
            {
                ["subject"] = "Distributor portal document",
                ["filename"] = fileName,
                ["mimetype"] = "application/pdf",
                ["documentbody"] = Convert.ToBase64String(pdfBytes),
                ["notetext"] = "PDF uploaded through the distributor portal.",
                ["objectid"] = new EntityReference("account", accountId)
            };
            var accountUpdate = new Entity("account", accountId)
            {
                [statusColumn] = new OptionSetValue(waitingForApprovalStatusValue)
            };
            var transactionResponse = (ExecuteTransactionResponse)service.Execute(new ExecuteTransactionRequest
            {
                Requests = new OrganizationRequestCollection
                {
                    new CreateRequest { Target = note },
                    new UpdateRequest { Target = accountUpdate }
                },
                ReturnResponses = true
            });

            var noteId = ((CreateResponse)transactionResponse.Responses[0]).id;
            return await NotifyDocumentUploadedAsync(accountId, noteId, cancellationToken);
        }
        catch (Exception ex) { throw Failure("Dataverse could not attach the document to this account.", ex); }
    }

    private async Task<bool> NotifyDocumentUploadedAsync(Guid accountId, Guid noteId, CancellationToken cancellationToken)
    {
        var workflowUrl = configuration["Crm:DocumentUploadWorkflowUrl"];
        if (string.IsNullOrWhiteSpace(workflowUrl) || !Uri.TryCreate(workflowUrl, UriKind.Absolute, out var baseUri))
        {
            logger.LogError("Document upload workflow URL is not configured as a valid absolute URL.");
            return false;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["AccountID"] = accountId.ToString(),
                ["NotesID"] = noteId.ToString()
            });
            logger.LogInformation("Calling CRM document workflow. AccountID={AccountID}; NotesID={NotesID}; RequestBody={RequestBody}",
                accountId, noteId, payload);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await httpClientFactory.CreateClient().PostAsync(baseUri, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("CRM document workflow accepted the request. StatusCode={StatusCode}; AccountID={AccountID}; NotesID={NotesID}",
                    (int)response.StatusCode, accountId, noteId);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            const int maxLoggedResponseLength = 4000;
            if (responseBody.Length > maxLoggedResponseLength)
                responseBody = responseBody[..maxLoggedResponseLength] + "…[truncated]";
            logger.LogError("CRM document workflow rejected the request. StatusCode={StatusCode}; Reason={ReasonPhrase}; AccountID={AccountID}; NotesID={NotesID}; ResponseBody={ResponseBody}",
                (int)response.StatusCode, response.ReasonPhrase, accountId, noteId, responseBody);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not invoke the document upload workflow for Account {AccountId} and Note {NoteId}.", accountId, noteId);
            return false;
        }
    }

    public Task<PortalContactProfile> CreateRelatedAsync(Guid accountId, Guid portalContactId, RelatedContactRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = Connect();
        try
        {
            var portalContact = service.Retrieve("contact", portalContactId, new ColumnSet("address1_country"));
            var account = service.Retrieve("account", accountId, new ColumnSet("name"));
            var fullName = JoinName(request.FirstName, request.LastName);
            var businessReference = new Entity("ssf_businessreferences")
            {
                ["ssf_name"] = fullName,
                ["ssf_company"] = Text(account, "name"),
                ["ssf_email"] = request.Email.Trim(),
                ["ssf_phone"] = request.Phone?.Trim(),
                ["ssf_jobtitle"] = request.JobTitle?.Trim(),
                ["ssf_country"] = Text(portalContact, "address1_country"),
                ["ssf_distributor"] = new EntityReference("account", accountId)
            };
            var id = service.Create(businessReference);
            return Task.FromResult(new PortalContactProfile(id, request.FirstName.Trim(), request.LastName.Trim(),
                fullName, request.Email.Trim(), request.Phone, request.JobTitle?.Trim()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dataverse rejected Business Reference creation for Account {AccountId}.", accountId);
            throw Failure("Dataverse could not add this Business Reference.", ex);
        }
    }

    public Task UpdateRelatedAsync(Guid accountId, Guid portalContactId, Guid businessReferenceId, RelatedContactRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = Connect();
        try
        {
            EnsureBusinessReferenceRelated(service, accountId, businessReferenceId);
            var portalContact = service.Retrieve("contact", portalContactId, new ColumnSet("address1_country"));
            var account = service.Retrieve("account", accountId, new ColumnSet("name"));
            service.Update(new Entity("ssf_businessreferences", businessReferenceId)
            {
                ["ssf_name"] = JoinName(request.FirstName, request.LastName),
                ["ssf_email"] = request.Email.Trim(), ["ssf_phone"] = request.Phone?.Trim(),
                ["ssf_jobtitle"] = request.JobTitle?.Trim(),
                ["ssf_company"] = Text(account, "name"),
                ["ssf_country"] = Text(portalContact, "address1_country")
            });
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dataverse rejected Business Reference update {BusinessReferenceId} for Account {AccountId}.", businessReferenceId, accountId);
            throw Failure("Dataverse could not update this Business Reference.", ex);
        }
    }

    public Task DeleteRelatedAsync(Guid accountId, Guid contactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = Connect();
        try
        {
            EnsureBusinessReferenceRelated(service, accountId, contactId);
            service.Delete("ssf_businessreferences", contactId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dataverse rejected Business Reference deletion {BusinessReferenceId} for Account {AccountId}.", contactId, accountId);
            throw Failure("Dataverse could not delete this Business Reference.", ex);
        }
    }

    private ServiceClient Connect()
    {
        var crm = configuration.GetSection("Crm");
        var url = Required(crm["Url"], "Crm:Url").TrimEnd('/');
        var clientId = Required(crm["ClientId"], "Crm:ClientId");
        var clientSecret = Required(crm["ClientSecret"], "Crm:ClientSecret");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new CrmIntegrationException("CRM setting 'Crm:Url' must be a valid HTTPS organization URL.");
        var service = new ServiceClient(uri, clientId, clientSecret, true, null);
        if (!service.IsReady)
        {
            var detail = service.LastError ?? "No additional error details were returned.";
            service.Dispose();
            throw Failure("Dataverse connection failed.", new InvalidOperationException(detail));
        }
        return service;
    }

    private static void EnsureBusinessReferenceRelated(ServiceClient service, Guid accountId, Guid businessReferenceId)
    {
        var reference = service.Retrieve("ssf_businessreferences", businessReferenceId, new ColumnSet("ssf_distributor"));
        var account = reference.GetAttributeValue<EntityReference>("ssf_distributor");
        if (account?.LogicalName != "account" || account.Id != accountId)
            throw new CrmIntegrationException("The requested Business Reference is not related to this Account.");
    }

    private static Guid GetLinkedAccountId(ServiceClient service, Guid contactId)
    {
        var contact = service.Retrieve("contact", contactId, new ColumnSet("parentcustomerid"));
        var account = contact.GetAttributeValue<EntityReference>("parentcustomerid");
        if (account?.LogicalName != "account")
            throw new CrmIntegrationException("The portal user is not linked to an Account.");
        return account.Id;
    }

    private static string FindInvitationStatusColumn(ServiceClient service, int statusValue, string statusLabel)
    {
        var metadata = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = "account",
            EntityFilters = EntityFilters.Attributes,
            RetrieveAsIfPublished = true
        });

        var candidates = metadata.EntityMetadata.Attributes
            .OfType<EnumAttributeMetadata>()
            .Where(attribute => attribute.OptionSet?.Options.Any(option =>
                option.Value == statusValue
                && option.Label?.LocalizedLabels.Any(label =>
                    string.Equals(label.Label, statusLabel, StringComparison.OrdinalIgnoreCase)) == true) == true)
            .Select(attribute => attribute.LogicalName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.Length == 1
            ? candidates[0]!
            : throw new CrmIntegrationException(candidates.Length == 0
                ? $"No Account Choice column contains the '{statusLabel}' option value {statusValue}."
                : $"More than one Account Choice column contains the '{statusLabel}' option. Configure the invitation status logical name to avoid updating the wrong field.");
    }

    private static PortalContactProfile ToContact(Entity contact) => new(
        contact.Id, Text(contact, "firstname"), Text(contact, "lastname"), Text(contact, "fullname"),
        Text(contact, "emailaddress1"), Text(contact, "telephone1"), Text(contact, "jobtitle"));

    private static PortalContactProfile ToBusinessReference(Entity reference)
    {
        var fullName = Text(reference, "ssf_name").Trim();
        var splitAt = fullName.IndexOf(' ');
        var firstName = splitAt < 0 ? fullName : fullName[..splitAt];
        var lastName = splitAt < 0 ? "" : fullName[(splitAt + 1)..].Trim();
        return new PortalContactProfile(reference.Id, firstName, lastName, fullName,
            Text(reference, "ssf_email"), Text(reference, "ssf_phone"), Text(reference, "ssf_jobtitle"));
    }

    private static string JoinName(string firstName, string lastName) => $"{firstName.Trim()} {lastName.Trim()}".Trim();

    private static string Text(Entity entity, string attribute) => entity.GetAttributeValue<string>(attribute) ?? "";
    private static string Required(string? value, string key) => string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal)
        ? throw new CrmIntegrationException($"CRM integration setting '{key}' is not configured.") : value;
    private static CrmIntegrationException Failure(string message, Exception ex) =>
        ex is CrmIntegrationException crm ? crm : new CrmIntegrationException(message, ex);
}
