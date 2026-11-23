# MKAsh PMS

The ministry's project management system. It grew out of the MKAsh PMS dashboard prototype and keeps its rules, figures and workflows. Those are now backed by a SQL Server database, ministry sign-in and an API, so the system can run in-house.

| Part | Technology | Folder |
|---|---|---|
| Business rules (progress, status, weighting, KPIs, reporting periods, permissions, workflows) | .NET 8 class library | `src/MKAsh.Core` |
| API (sign-in, data, approvals, change requests, closures, KPIs, reports) | ASP.NET Core 8 + EF Core | `src/MKAsh.Api` |
| Web app (English / Arabic, role-based menus) | React + Vite + TypeScript | `web` |
| Database | SQL Server 2019+ | created by the API on first start |
| Starting data (50 PQI projects, plans, KPIs, strategy roadmap) | JSON | `database/seed` |
| NDS3 change-request Excel template | .xlsm | `templates` |
| Tests that prove the system matches the prototype's figures | xUnit | `tests/MKAsh.Core.Tests` |

## Run it on a developer machine

You need the .NET 8 SDK, Node 20 and SQL Server. SQL Server can be Docker, LocalDB or a dev server.

```bash
# 1. SQL Server in Docker (skip if you have one)
docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Dev_Passw0rd!' -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

# 2. API: creates the database and loads database/seed on first start
cd src/MKAsh.Api
dotnet run            # http://localhost:5080  (Swagger at /swagger)

# 3. Web app
cd web
cp .env.example .env  # VITE_AUTH_MODE=dev
npm install
npm run dev           # http://localhost:5173
```

In development mode the sign-in page lists demo users: PMO, executive, sector leads and a project manager. **Switch user** signs out so you can sign in as someone else and see what each role sees. The demo users come from `database/seed/people.example.json`.

## Real accounts

1. Create `database/seed/people.json` in the same format as `people.example.json`, with the ministry emails. Git ignores this file.
2. Register two apps in Microsoft Entra ID, one for the API and one for the web app, then fill in `AzureAd` in `appsettings.json` and the `VITE_ENTRA_*` values. Step-by-step instructions are in [docs/setup.md](docs/setup.md).
3. Set `Auth:Mode` to `EntraId`. This is the default outside Development.

## Tests

```bash
dotnet test
```

`GoldenTests` recalculates progress, planned progress and status for all 50 projects in both progress views, plus every strategic KPI. It checks the results against figures recorded from the prototype (`tests/MKAsh.Core.Tests/golden.json`, recorded on 23 Nov 2026), so any rule change that alters a figure fails the build.

GitHub Actions runs the .NET build, the tests and the web build on every push (`.github/workflows/ci.yml`).

## Documentation

- [Architecture](docs/architecture.md)
- [Roles and permissions](docs/roles.md)
- [Business rules](docs/business-rules.md)
- [Setup and deployment](docs/setup.md)
