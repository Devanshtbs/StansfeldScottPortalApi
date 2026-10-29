using Microsoft.Extensions.Configuration;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using StansfeldScott.Portal.Common.Exceptions;
using StansfeldScott.Portal.Domain.Contracts;
using StansfeldScott.Portal.Infrastructure.Security;

namespace StansfeldScott.Portal.Infrastructure.ExternalClients;

/// <summary>Checks portal credentials against an active Dataverse Contact.</summary>
public sealed class DataversePortalSignInService(IConfiguration configuration)
{
    private const int ApprovedInvitationStatus = 924060004;

    public Task<PortalSignInResult?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var crm = configuration.GetSection("Crm");
        var organizationUrl = Required(crm["Url"], "Crm:Url").TrimEnd('/');
        var clientId = Required(crm["ClientId"], "Crm:ClientId");
        var clientSecret = Required(crm["ClientSecret"], "Crm:ClientSecret");
        var usernameColumn = Required(crm["UsernameColumn"], "Crm:UsernameColumn");
        var passwordColumn = Required(crm["PasswordColumn"], "Crm:PasswordColumn");

        if (!Uri.TryCreate(organizationUrl, UriKind.Absolute, out var organizationUri)
            || organizationUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new CrmIntegrationException("CRM setting 'Crm:Url' must be a valid HTTPS organization URL.");
        }

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
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid", "fullname", "parentcustomerid", passwordColumn),
                TopCount = 2
            };
            query.Criteria.AddCondition(usernameColumn, ConditionOperator.Equal, username.Trim());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var accountLink = query.AddLink(
                "account", "parentcustomerid", "accountid", JoinOperator.LeftOuter);
            accountLink.EntityAlias = "account";
            accountLink.Columns = new ColumnSet("name");

            var matches = service.RetrieveMultiple(query).Entities;
            if (matches.Count != 1)
                return Task.FromResult<PortalSignInResult?>(null);

            var contact = matches[0];
            var storedPassword = contact.GetAttributeValue<string>(passwordColumn);
            if (string.IsNullOrEmpty(storedPassword)
                || !PortalPasswordHasher.VerifyAndUpgrade(storedPassword, password, out var upgradedHash))
            {
                return Task.FromResult<PortalSignInResult?>(null);
            }

            if (upgradedHash is not null)
            {
                service.Update(new Entity("contact", contact.Id)
                {
                    [passwordColumn] = upgradedHash
                });
            }

            var parentAccount = contact.GetAttributeValue<EntityReference>("parentcustomerid");
            var accountName = contact.GetAttributeValue<AliasedValue>("account.name")?.Value as string;

            return Task.FromResult<PortalSignInResult?>(new PortalSignInResult(
                contact.Id,
                contact.GetAttributeValue<string>("fullname") ?? username.Trim(),
                parentAccount?.LogicalName == "account" ? parentAccount.Id : null,
                accountName));
        }
        catch (Exception ex) when (ex is not CrmIntegrationException)
        {
            throw new CrmIntegrationException(
                "Dataverse could not validate the portal credentials. Check the backend log for field or permission details.", ex);
        }
    }

    public Task<bool> CanAccessWorkspaceAsync(Guid contactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var crm = configuration.GetSection("Crm");
        var organizationUrl = Required(crm["Url"], "Crm:Url").TrimEnd('/');
        var clientId = Required(crm["ClientId"], "Crm:ClientId");
        var clientSecret = Required(crm["ClientSecret"], "Crm:ClientSecret");

        if (!Uri.TryCreate(organizationUrl, UriKind.Absolute, out var organizationUri)
            || organizationUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new CrmIntegrationException("CRM setting 'Crm:Url' must be a valid HTTPS organization URL.");
        }

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
            var contact = service.Retrieve("contact", contactId, new ColumnSet("parentcustomerid"));
            var accountRef = contact.GetAttributeValue<EntityReference>("parentcustomerid");
            if (accountRef?.LogicalName != "account")
                return Task.FromResult(false);

            var account = service.Retrieve("account", accountRef.Id, new ColumnSet("ssf_invitationstatus"));
            var status = account.GetAttributeValue<OptionSetValue>("ssf_invitationstatus");
            return Task.FromResult(status?.Value == ApprovedInvitationStatus);
        }
        catch (Exception ex) when (ex is not CrmIntegrationException)
        {
            throw new CrmIntegrationException(
                "Dataverse could not verify the account approval status. Check the backend log for field or permission details.", ex);
        }
    }

    private static string Required(string? value, string key) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal)
            ? throw new CrmIntegrationException($"CRM integration setting '{key}' is not configured.")
            : value;
}
