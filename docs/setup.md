# Setup and deployment

## 1. Database
SQL Server 2019 or later. Create an empty database, for example `MKAshPMS`, and a login the server can use. Set the connection string as a secret or environment variable:

```
ConnectionStrings__Pms="Server=sql01;Database=MKAshPMS;User Id=mkash_app;Password=…;TrustServerCertificate=True"
```

The server creates its two tables, `Docs` and `Assets`, on first start.

## 2. First data load
- Start once with `Seed__OnStartup=true`. This loads the 50 PQI projects and work plans from `database/seed/docs/seed.json`, but only if the database is empty.
- People and roles: either add them on the **Users** page after signing in as an owner, or put an `access.json` file in `database/seed/docs`. Use the same format as `access.example.json`. Git ignores this file because it holds ministry emails.
- Set `Seed__OnStartup=false` afterwards.

## 2b. Moving data from the dashboard
Sign in as an owner or PMO, open `/system/import.html`, and import `tamkeen-migration.json`. It contains the dashboard's projects, work plans, users, project-manager directory and change requests. Then open **Overview** and check the totals: 50 active projects, QAR 263,046,274.

## 3. Sign-in with ministry accounts (Microsoft Entra ID)
In the ministry tenant, go to the Entra admin centre → **App registrations → New registration**, named "Tamkeen":
- Platform: **Web**, redirect URI `https://<your-host>/signin-oidc`, front-channel logout URL `https://<your-host>/signout-callback-oidc`.
- Under **Certificates & secrets**, create a client secret. In production, a certificate is better.
- Configure the server:
  ```
  Auth__Mode=EntraId
  AzureAd__TenantId=<tenant id>
  AzureAd__ClientId=<application id>
  AzureAd__ClientSecret=<secret>
  Pms__Owners__0=<head of PMO email>
  ```
- Owners get full rights and the switch-user preview. Everyone else gets the role set for their email on the **Users** page.
- People who sign in with their ministry account go straight in, with no second password.

## 4. Build and host
```bash
dotnet publish src/MKAsh.Api -c Release -o out     # includes the screens (web/) in wwwroot
```
Host `out/` on IIS (with the ASP.NET Core Hosting Bundle) or in a container, behind HTTPS. Uploaded files are stored in the database, up to 20 MB each. Include the database in the normal backups.

## Configuration reference
| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Pms` | — | Database connection |
| `Database:Provider` | `SqlServer` | `Sqlite` is for quick tests only (Codespaces) |
| `Auth:Mode` | `EntraId` | `Development` gives the test sign-in form; the server refuses this outside Development |
| `AzureAd:*` | — | Entra ID app registration |
| `Pms:Owners` | `[]` | Emails with owner rights |
| `Dev:FirstUserIsOwner` | `true` | Test systems: the first person to sign in becomes the owner |
| `Seed:OnStartup` | `false` | Load `database/seed/docs` into an empty database |
| `Database:MigrateOnStartup` | `true` | Create the tables at start-up |
