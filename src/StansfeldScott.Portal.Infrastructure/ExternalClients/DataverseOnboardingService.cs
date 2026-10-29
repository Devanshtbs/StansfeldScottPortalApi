using Microsoft.Extensions.Configuration;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using StansfeldScott.Portal.Common.Exceptions;
using StansfeldScott.Portal.Domain.Contracts;
using StansfeldScott.Portal.Infrastructure.Security;

namespace StansfeldScott.Portal.Infrastructure.ExternalClients;

/// <summary>Writes distributor access requests to the configured Dataverse organization.</summary>
public sealed class DataverseOnboardingService(
    IConfiguration configuration,
    DataverseInvitationValidationService invitations)
{
    public async Task SubmitAsync(AccessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var crm = configuration.GetSection("Crm");
        var organizationUrl = Required(crm["Url"], "Crm:Url").TrimEnd('/');
        var clientId = Required(crm["ClientId"], "Crm:ClientId");
        var clientSecret = Required(crm["ClientSecret"], "Crm:ClientSecret");
        var registeredStatusValue = int.TryParse(crm["RegisteredInvitationStatusValue"], out var configuredStatus)
            ? configuredStatus : 924060002;
        var usernameColumn = Required(crm["UsernameColumn"], "Crm:UsernameColumn");
        var passwordColumn = Required(crm["PasswordColumn"], "Crm:PasswordColumn");

        if (!Uri.TryCreate(organizationUrl, UriKind.Absolute, out var organizationUri)
            || organizationUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new CrmIntegrationException("CRM setting 'Crm:Url' must be a valid HTTPS organization URL.");
        }

        // Use the typed client-secret constructor to avoid connection-string parsing
        // ambiguities when secret values contain punctuation.
        using var service = new ServiceClient(organizationUri, clientId, clientSecret, true, null);
        if (!service.IsReady)
        {
            var sdkError = service.LastError ?? "No additional error details were returned.";
            throw new CrmIntegrationException(
                "Dataverse connection failed. Check the backend log for the SDK error.",
                new InvalidOperationException(sdkError));
        }

        try
        {
            if (request.AccountId is null || !await invitations.ValidateAsync(
                    request.AccountId.Value, request.InvitationCode, cancellationToken))
            {
                throw new ValidationException("This invitation is invalid or expired. Please use the invitation link again.");
            }

            var duplicateUsernameQuery = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid"),
                TopCount = 1
            };
            duplicateUsernameQuery.Criteria.AddCondition(
                usernameColumn, ConditionOperator.Equal, request.Username.Trim());
            if (service.RetrieveMultiple(duplicateUsernameQuery).Entities.Count > 0)
            {
                throw new ValidationException("That username is already in use. Please choose another one.");
            }

            var accountId = request.AccountId.Value;
            var account = service.Retrieve("account", accountId, new ColumnSet("accountid", "ownerid"));
            var accountOwner = account.GetAttributeValue<EntityReference>("ownerid")
                ?? throw new CrmIntegrationException("The Account has no owner, so the portal Contact owner cannot be assigned.");
            var invitationStatusColumn = FindInvitationStatusColumn(service, registeredStatusValue);
            var passwordHash = PortalPasswordHasher.Hash(request.Password);

            var contact = new Entity("contact")
            {
                ["firstname"] = request.FirstName.Trim(),
                ["lastname"] = request.LastName.Trim(),
                ["emailaddress1"] = request.Email,
                ["telephone1"] = request.Phone,
                ["jobtitle"] = request.JobTitle,
                [usernameColumn] = request.Username.Trim(),
                [passwordColumn] = passwordHash,
                ["and_portaluser"] = false,
                ["parentcustomerid"] = new EntityReference("account", accountId),
                ["ownerid"] = accountOwner,
                ["address1_country"] = request.Region.Trim()
            };
            var countryLookupColumn = FindCountryLookupColumn(service);
            if (countryLookupColumn is not null)
                contact[countryLookupColumn] = new EntityReference("and_country", request.CountryId!.Value);

            account[invitationStatusColumn] = new OptionSetValue(registeredStatusValue);
            var transaction = new ExecuteTransactionRequest
            {
                Requests = new OrganizationRequestCollection
                {
                    new CreateRequest { Target = contact },
                    new UpdateRequest { Target = account }
                },
                ReturnResponses = false
            };
            service.Execute(transaction);
        }
        catch (Exception ex) when (ex is not CrmIntegrationException && ex is not ValidationException)
        {
            throw new CrmIntegrationException(
                "Dataverse rejected the access request. Check the backend log for field or permission details.", ex);
        }

    }

    private static string Required(string? value, string key) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal)
            ? throw new CrmIntegrationException($"CRM integration setting '{key}' is not configured.")
            : value;

    private static string FindInvitationStatusColumn(ServiceClient service, int registeredStatusValue)
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
                option.Value == registeredStatusValue
                && option.Label?.LocalizedLabels.Any(label =>
                    string.Equals(label.Label, "Registered", StringComparison.OrdinalIgnoreCase)) == true) == true)
            .Select(attribute => attribute.LogicalName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.Length == 1
            ? candidates[0]!
            : throw new CrmIntegrationException(candidates.Length == 0
                ? $"No Account Choice column contains the 'Registered' option value {registeredStatusValue}."
                : "More than one Account Choice column contains the Registered option. Configure the invitation status logical name to avoid updating the wrong field.");
    }

    private static string? FindCountryLookupColumn(ServiceClient service)
    {
        var metadata = (RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
        {
            LogicalName = "contact",
            EntityFilters = EntityFilters.Attributes,
            RetrieveAsIfPublished = true
        });
        var matches = metadata.EntityMetadata.Attributes
            .OfType<LookupAttributeMetadata>()
            .Where(attribute => attribute.Targets?.Contains("and_country", StringComparer.OrdinalIgnoreCase) == true)
            .Select(attribute => attribute.LogicalName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new CrmIntegrationException("The Contact table has multiple Country lookups to and_country. Configure the intended lookup field to avoid selecting the wrong relationship.")
        };
    }

}
