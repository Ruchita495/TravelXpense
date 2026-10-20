# Measure your own numbers before they go on a resume

Interviewers will ask "how did you measure that?". Only claim numbers you measured on THIS code.

| Claim | How to measure it for real |
|---|---|
| Number of tests | `dotnet test` prints the total. (The repo ships about 21 test cases; add more as you extend it.) |
| Code coverage | `dotnet test --collect:"XPlat Code Coverage"` then `dotnet tool install -g dotnet-reportgenerator-globaltool` and `reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:coverage` |
| Search latency with vs without cache | Call `/api/flights/search` 20 times with the **Amadeus** provider on (real network latency), once with Redis cache and once without. Compare median/p95 using k6 (`k6 run`) or a loop with `curl -w "%{time_total}"`. With the mock provider the difference is tiny, so don't quote it. |
| Release time | Time a manual build+deploy by hand once; compare with the GitHub Actions run duration shown in the Actions tab. |
| Security scan | `docker run --rm -t ghcr.io/zaproxy/zaproxy:stable zap-api-scan.py -t http://host.docker.internal:5000/swagger/v1/swagger.json -f openapi` and report what it actually finds. |
| Messages processed | Write a tiny script that submits N reports and count rows in `ProcessedMessages` / reports with status != Submitted. |

## Honest resume bullets for the project as shipped
- Built a .NET 8 REST API with JWT/RBAC, EF Core on SQL Server, and a Redis-cached third-party flight-search adapter with retry/circuit-breaker policies.
- Implemented an event-driven expense workflow (queue + idempotent consumer, Azure Service Bus ready) with a unit-tested policy engine and optional Claude-based expense categorization.
- Wrote xUnit unit and WebApplicationFactory integration tests; GitHub Actions pipeline builds, tests and containerizes the API.
- Applied OWASP Top 10 controls: parameterized queries (EF Core), BCrypt hashing, server-side price verification, object-level authorization (no IDOR), generic login errors, security headers.
Add numbers (coverage %, latency change, release time) only after measuring them with the table above.
