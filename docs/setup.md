# Setup and deployment

## 1. Database
SQL Server 2019 or later. Create an empty database, for example `MKAshPMS`, and a login the API can use. Put the connection string in `ConnectionStrings:Pms`, using an environment variable or a secret store, not `appsettings.json`:

```
ConnectionStrings__Pms="Server=sql01;Database=MKAshPMS;User Id=mkash_api;Password=…;TrustServerCertificate=True"
```

The API creates the tables on first start. For controlled upgrades, add EF Core migrations once:

```bash
dotnet tool install --global dotnet-ef
cd src/MKAsh.Api
dotnet ef migrations add Initial
```

From then on the API applies pending migrations at start-up. To apply them by hand instead, set `Database:MigrateOnStartup=false`.

## 2. First data load
1. Copy `database/seed/people.example.json` to `database/seed/people.json` and replace the demo users with the real people, roles and emails.
   - Sector leads need `leadName` exactly as it appears on projects.
   - Project managers' Arabic names must match the manager names on projects, so they are linked automatically.
2. Start the API once with `Seed__OnStartup=true`. It loads projects, work plans, KPIs, strategy and people only if the database has no projects.
3. Set `Seed__OnStartup=false` afterwards.

## 3. Microsoft Entra ID sign-in
In the ministry tenant (Entra admin centre → App registrations):

**API app: "MKAsh PMS API"**
- Expose an API: set the Application ID URI to `api://<api-client-id>` and add the scope `access_as_user`.
- Set `AzureAd:TenantId`, `AzureAd:ClientId` and `AzureAd:Audience` in the API configuration.

**Web app: "MKAsh PMS"**
- Platform: Single-page application, redirect URI `https://<your-host>/`.
- API permissions: add `MKAsh PMS API / access_as_user` and grant admin consent.
- Build the web app with:
  ```
  VITE_AUTH_MODE=entra
  VITE_ENTRA_CLIENT_ID=<web-client-id>
  VITE_ENTRA_TENANT_ID=<tenant-id>
  VITE_API_SCOPE=api://<api-client-id>/access_as_user
  ```

People are matched to the `People` table by the email (UPN) in their token. Anyone signed in but not on that list gets "not registered".

## 4. Build and host
```bash
cd web && npm ci && npm run build     # outputs into src/MKAsh.Api/wwwroot
cd .. && dotnet publish src/MKAsh.Api -c Release -o out
```
Host `out/` on IIS (with the ASP.NET Core Hosting Bundle) or in a container. The API serves the web app and `/api` from the same address, so no CORS set-up is needed in production. Keep `templates/` and `database/seed/` beside the app, or point `ChangeRequests:Template` and `Seed:Directory` at their locations.

## Configuration reference
| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Pms` | — | SQL Server connection |
| `Auth:Mode` | `EntraId` | `Development` allows the demo X-Dev-User sign-in; the API refuses this outside Development |
| `AzureAd:*` | — | Entra ID API registration |
| `Database:MigrateOnStartup` | `true` | Create or migrate the database at start-up |
| `Seed:OnStartup` | `false` | Load `database/seed` into an empty database |
| `Seed:Directory` | `../../database/seed` | Seed folder |
| `ChangeRequests:Template` | `../../templates/Change_Request_Template.xlsm` | NDS3 Excel template |
| `Cors:Origins` | `http://localhost:5173` | Only needed when the web app is served separately |
