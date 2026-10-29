using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace StansfeldScott.Portal.Infrastructure.ExternalClients;


public class CrmApiClient
{
    private readonly HttpClient _httpClient;

    public CrmApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        var baseUrl = configuration["Crm:ApiManagementBaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        var subscriptionKey = configuration["Crm:SubscriptionKey"];
        if (!string.IsNullOrWhiteSpace(subscriptionKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        }
    }

    public async Task<TResponse?> GetAsync<TResponse>(string relativePath)
    {
        var response = await _httpClient.GetAsync(relativePath);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativePath, TRequest payload)
    {
        var response = await _httpClient.PostAsJsonAsync(relativePath, payload);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string relativePath, TRequest payload)
    {
        var response = await _httpClient.PutAsJsonAsync(relativePath, payload);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }
}
