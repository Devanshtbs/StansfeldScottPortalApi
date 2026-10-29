using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using StansfeldScott.Portal.Common.Middleware;
using StansfeldScott.Portal.Infrastructure.Data;
using StansfeldScott.Portal.Infrastructure.ExternalClients;

var builder = WebApplication.CreateBuilder(args);


var azureAd = builder.Configuration.GetSection("AzureAd");
var entraInstance = azureAd["Instance"];
var entraConfigured = IsConfiguredValue(azureAd["TenantId"])
    && IsConfiguredValue(azureAd["ClientId"])
    && IsConfiguredValue(azureAd["Audience"])
    && Uri.TryCreate(entraInstance, UriKind.Absolute, out var authorityUri)
    && authorityUri is not null
    && !authorityUri.Host.Contains('<');

if (entraConfigured)
{
    builder.Services
        .AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(azureAd);
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication();
}
else
{
    throw new InvalidOperationException("Valid AzureAd tenant, client, audience, and authority settings are required outside Development.");
}

builder.Services.AddAuthorization();


const string AngularCorsPolicy = "AngularClient";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularCorsPolicy, policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));


builder.Services.AddScoped<DataverseOnboardingService>();
builder.Services.AddScoped<DataverseInvitationValidationService>();
builder.Services.AddScoped<DataverseRegistrationDataService>();
builder.Services.AddScoped<DataversePortalSignInService>();
builder.Services.AddScoped<DataversePortalProfileService>();
builder.Services.AddHttpClient();



builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (!entraConfigured)
    app.Logger.LogWarning("Entra authentication is not configured. Running in Development with public routes only.");

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(AngularCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static bool IsConfiguredValue(string? value) =>
    !string.IsNullOrWhiteSpace(value) && !value.StartsWith("<", StringComparison.Ordinal);
