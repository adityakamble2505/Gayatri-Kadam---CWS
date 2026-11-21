# GreenCarWash

Seven ASP.NET Core 8 Web APIs. Each service has its own SQL Server database. This repository currently contains solution wiring only: health checks, Swagger, JWT, internal service auth, Serilog, and the shared outbox poller. Business endpoints are not implemented yet.

`Docs/schema.sql` is the database contract. Do not change it to match code.

## Prerequisites

- .NET 8 SDK
- SQL Server (default instance, Windows authentication)
- Docker Desktop (MailHog only)

## Run

1. Create the seven databases and load the seed. `GCW_SQL_SERVER` defaults to `localhost`.

   ```powershell
   ./scripts/setup-db.ps1
   ```

2. Start MailHog.

   ```bash
   docker compose up -d
   ```

3. Build the solution.

   ```bash
   dotnet build GreenCarWash.sln
   ```

4. Run a service from the repository root. The Development profile turns Swagger on.

   ```bash
   dotnet run --project src/AuthService
   ```

5. Check the process:

   - Health: `http://localhost:7000/health`
   - Swagger: `http://localhost:7000/swagger`
   - MailHog UI: `http://localhost:8025` (SMTP on port 1025)

Repeat `dotnet run --project` for the other services. Use a separate terminal per service.

| Project | Port | Database | Health |
| --- | --- | --- | --- |
| AuthService | 7000 | AuthDB | http://localhost:7000/health |
| CustomerService | 7001 | CustomerDB | http://localhost:7001/health |
| WasherService | 7002 | WasherDB | http://localhost:7002/health |
| CatalogService | 7003 | CatalogDB | http://localhost:7003/health |
| BookingService | 7004 | BookingDB | http://localhost:7004/health |
| PaymentService | 7005 | PaymentDB | http://localhost:7005/health |
| NotificationService | 7006 | NotificationDB | http://localhost:7006/health |

Each service uses Windows authentication, for example `Server=localhost;Database=AuthDB;Trusted_Connection=True;TrustServerCertificate=True`. Set `GCW_SQL_SERVER` to point at another instance. Set `GCW_SQL_USER` and `GCW_SQL_PASSWORD` to use SQL authentication instead. `Jwt:Key` and `InternalApi:Key` are local development placeholders shared by every service. Replace them before any shared or deployed environment.

To drop and recreate the seven databases, set the environment to Development and run `scripts/reset-db.ps1`. The script refuses to run in any other environment.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
./scripts/reset-db.ps1
```
"# Gayatri-Kadam---CWS" 
