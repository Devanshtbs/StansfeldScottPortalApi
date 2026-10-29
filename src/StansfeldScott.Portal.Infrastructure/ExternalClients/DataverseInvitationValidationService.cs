using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using StansfeldScott.Portal.Common.Exceptions;

namespace StansfeldScott.Portal.Infrastructure.ExternalClients;

/// <summary>Validates an invitation code stored on a Dataverse Account.</summary>
public sealed class DataverseInvitationValidationService(IConfiguration configuration)
{
    public Task<bool> ValidateAsync(Guid accountId, string invitationCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var crm = configuration.GetSection("Crm");
        var organizationUrl = Required(crm["Url"], "Crm:Url").TrimEnd('/');
        var clientId = Required(crm["ClientId"], "Crm:ClientId");
        var clientSecret = Required(crm["ClientSecret"], "Crm:ClientSecret");
        var codeColumn = Required(crm["InvitationCodeColumn"], "Crm:InvitationCodeColumn");
        var expiryColumn = crm["InvitationExpiryColumn"];

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
            var columns = string.IsNullOrWhiteSpace(expiryColumn)
                ? new[] { codeColumn }
                : new[] { codeColumn, expiryColumn };
            var query = new QueryExpression("account")
            {
                ColumnSet = new ColumnSet(columns),
                TopCount = 1
            };
            query.Criteria.AddCondition("accountid", ConditionOperator.Equal, accountId);

            var account = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            if (account is null
                || !account.Attributes.TryGetValue(codeColumn, out var storedValue)
                || storedValue is not string storedCode
                || !CodesMatch(storedCode, invitationCode))
            {
                return Task.FromResult(false);
            }

            if (!string.IsNullOrWhiteSpace(expiryColumn))
            {
                if (!account.Attributes.TryGetValue(expiryColumn, out var expiryValue)
                    || expiryValue is not DateTime expiry
                    || expiry <= DateTime.UtcNow)
                {
                    return Task.FromResult(false);
                }
            }

            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is not CrmIntegrationException)
        {
            throw new CrmIntegrationException(
                "Dataverse could not validate the invitation. Check the backend log for field or permission details.", ex);
        }
    }

    private static bool CodesMatch(string storedCode, string submittedCode)
    {
        var storedBytes = Encoding.UTF8.GetBytes(storedCode.Trim().ToUpperInvariant());
        var submittedBytes = Encoding.UTF8.GetBytes(submittedCode.Trim().ToUpperInvariant());
        return storedBytes.Length == submittedBytes.Length
            && CryptographicOperations.FixedTimeEquals(storedBytes, submittedBytes);
    }

    private static string Required(string? value, string key) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal)
            ? throw new CrmIntegrationException($"CRM integration setting '{key}' is not configured.")
            : value;
}
