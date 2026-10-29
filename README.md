# Backend configuration

Sensitive runtime settings are intentionally not stored in `appsettings.json`.

For local development, use .NET User Secrets from the solution folder:

```powershell
dotnet user-secrets init --project src/StansfeldScott.Portal.Api
dotnet user-secrets set "Crm:ClientSecret" "<new-client-secret>" --project src/StansfeldScott.Portal.Api
dotnet user-secrets set "Crm:DocumentUploadWorkflowUrl" "<regenerated-workflow-url>" --project src/StansfeldScott.Portal.Api
```

For IIS, configure these environment variables for the application or its app pool:

- `Crm__ClientSecret`
- `Crm__DocumentUploadWorkflowUrl`

Do not commit secrets, signed workflow URLs, or published output folders. The current `appsettings.json` values for these settings are blank by design.