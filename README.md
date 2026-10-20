# TravelXpense - Corporate Travel Booking & Expense Management

A full-stack learning/portfolio project modeled on corporate travel + expense workflows
(trip request -> flight search -> booking -> expense report -> policy check -> manager approval).

| Layer | Tech |
|---|---|
| Frontend | React 18 + Vite |
| API | C# / .NET 8, ASP.NET Core Web API, JWT auth, Swagger |
| Database | SQL Server (Azure SQL compatible) via Entity Framework Core |
| Cache | Redis (falls back to in-memory if not configured) |
| Messaging | Azure Service Bus queue in the cloud, in-memory queue locally |
| 3rd-party APIs | Amadeus flight search (mock provider used if no keys), Claude API for expense categorization (keyword rules used if no key) |
| DevOps | Docker, Docker Compose, GitHub Actions CI (+ optional Azure App Service deploy) |
| Tests | xUnit unit tests + integration tests (WebApplicationFactory) |

## Architecture

```
 React (5173) --/api--> ASP.NET Core API (5000) --> SQL Server
                          |   |   \--> Redis (flight search cache)
                          |   \------> Amadeus API (adapter + retries/circuit breaker)
                          |
                          +--publish "ExpenseSubmitted"--> Queue (Service Bus / in-memory)
                                                              |
                                          Consumer (background service in the same app)
                                          1. auto-categorize lines (Claude or keyword rules)
                                          2. check policy rules
                                          3. status -> PendingApproval or Flagged
```

Report status flow: `Draft -> Submitted -> (PendingApproval | Flagged) -> Approved | Rejected`

## Quick start (Docker for the backend, npm for the frontend)

Prerequisites: Docker Desktop, Node.js 20+. (.NET 8 SDK only if you want to run tests or run the API without Docker.)

```bash
# 1) Backend + SQL Server + Redis
docker compose up --build        # first start takes a minute: SQL Server must boot
# API:     http://localhost:5000/swagger
# Health:  http://localhost:5000/health

# 2) Frontend (new terminal)
cd frontend
npm install
npm run dev                      # http://localhost:5173
```

Demo logins (password `Password123!`): `employee@demo.com`, `manager@demo.com`.


### Try the whole flow (5 minutes)
1. Log in as **employee@demo.com**. Create a trip (e.g. Toronto).
2. Click *Search & book a flight*, search YQG -> YYZ, book one (the last/most expensive is Business class).
3. Go to **Expenses**, create a report for the trip, add: `Starbucks 6.50`, and `Marriott Downtown 450` (leave category on Auto, no receipt).
4. Submit. The status changes to **Flagged** after a moment (over the lodging limit + missing receipt).
5. Log out, log in as **manager@demo.com** -> Approvals -> approve or reject.

## Run tests
```bash
cd backend
dotnet test
```

## Run the API without Docker
```bash
# needs SQL Server + (optionally) Redis running; edit connection string in appsettings.json
cd backend/src/TravelXpense.Api
dotnet run --urls http://localhost:5000
```

## Turn on the real services (optional)
Set these as environment variables (or in `docker-compose.yml`):
| Variable | Effect |
|---|---|
| `Amadeus__ClientId`, `Amadeus__ClientSecret` | Real flight search (free test keys: developers.amadeus.com) |
| `Anthropic__ApiKey` | Claude categorizes expenses instead of keyword rules |
| `Messaging__Provider=ServiceBus`, `ServiceBus__ConnectionString` | Use Azure Service Bus (queue `expense-events` must exist) |
| `Redis__ConnectionString` | Redis cache (already set in Compose) |

## Folder map
```
backend/src/TravelXpense.Api
  Controllers/   HTTP endpoints (Auth, Trips, Flights, Expenses, Approvals)
  Domain/        Entities + enums (the "nouns" of the business)
  Data/          EF Core DbContext + startup seeding
  Contracts/     Request/response shapes + mappers
  Security/      JWT creation, claims helpers
  Travel/        Flight provider adapters (Amadeus, mock) + caching service
  Messaging/     Events, queue publisher/consumers, event handler
  Policy/        Spending-rule engine (pure, unit-tested)
  Ai/            Expense categorizers (keyword + Claude)
backend/tests    xUnit tests
database/schema.sql   Same schema as plain SQL
frontend/src     React app
docs/            Metrics guide, Azure deploy notes
```

## API summary
| Method | Path | Who | Purpose |
|---|---|---|---|
| POST | /api/auth/register, /api/auth/login | anyone | get a JWT |
| GET/POST | /api/trips | employee | list / create own trips |
| POST | /api/trips/{id}/bookings | employee | book a flight (server re-verifies price) |
| GET | /api/flights/search?origin=&destination=&date= | employee | cached flight search |
| GET/POST | /api/expenses, /api/expenses/{id}/items | employee | reports and line items |
| POST | /api/expenses/{id}/submit | employee | queue the report for processing |
| GET | /api/approvals | manager/admin | reports awaiting a decision |
| POST | /api/approvals/{id}/approve, /reject | manager/admin | decide |

## Known limitations (good "what I'd improve next")
- **Outbox pattern:** the report is saved, then the event is published. If the app crashes between the two, the report stays "Submitted". A transactional outbox table fixes this.
- **EnsureCreated vs migrations:** fine for a demo; real projects use EF Core migrations.
- **Single currency (CAD):** no FX conversion.
- **Receipts are URLs**, not uploaded files (Azure Blob Storage would be the next step).
- **JWT in localStorage:** simple but exposed to XSS; httpOnly cookies are safer.
- **One deployable:** the consumer runs inside the API process. Splitting it into a separate worker service is a small change because it already only depends on the queue.
