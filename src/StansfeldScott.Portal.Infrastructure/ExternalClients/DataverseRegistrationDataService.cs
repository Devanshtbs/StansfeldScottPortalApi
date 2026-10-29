using Microsoft.Extensions.Configuration;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using StansfeldScott.Portal.Common.Exceptions;
using StansfeldScott.Portal.Domain.Contracts;

namespace StansfeldScott.Portal.Infrastructure.ExternalClients;

public sealed class DataverseRegistrationDataService(
    IConfiguration configuration,
    DataverseInvitationValidationService invitations)
{
    public async Task<InvitationRegistrationInfo?> GetInvitationInfoAsync(
        Guid accountId, string invitationCode, CancellationToken cancellationToken)
    {
        if (!await invitations.ValidateAsync(accountId, invitationCode, cancellationToken))
            return null;

        using var service = Connect();
        try
        {
            var account = service.Retrieve("account", accountId, new ColumnSet("name", "accountnumber"));
            return new InvitationRegistrationInfo(
                account.Id,
                account.GetAttributeValue<string>("name") ?? "",
                account.GetAttributeValue<string>("accountnumber") ?? "");
        }
        catch (Exception ex)
        {
            throw new CrmIntegrationException("Dataverse could not load the invited Account details.", ex);
        }
    }

    public Task<IReadOnlyList<CountryOption>> GetCountriesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = Connect();
        try
        {
            var query = new QueryExpression("and_country")
            {
                ColumnSet = new ColumnSet("and_name"),
                Criteria = new FilterExpression(LogicalOperator.And)
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder("and_name", OrderType.Ascending);
            IReadOnlyList<CountryOption> results = service.RetrieveMultiple(query).Entities
                .Select(country => new CountryOption(country.Id, country.GetAttributeValue<string>("and_name") ?? ""))
                .Where(country => !string.IsNullOrWhiteSpace(country.Name))
                .ToArray();
            return Task.FromResult(results);
        }
        catch (Exception ex)
        {
            throw new CrmIntegrationException("Dataverse could not load countries from and_country.", ex);
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
            throw new CrmIntegrationException("Dataverse connection failed.", new InvalidOperationException(detail));
        }
        return service;
    }

    private static string Required(string? value, string key) => string.IsNullOrWhiteSpace(value) || value.StartsWith("<", StringComparison.Ordinal)
        ? throw new CrmIntegrationException($"CRM integration setting '{key}' is not configured.") : value;
}
