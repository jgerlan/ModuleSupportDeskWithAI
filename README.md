# AI Support Desk

Support ticket management with Angular 22, ASP.NET Core 8 and SQL Server. Tickets support CRUD, search, filters, pagination and a dashboard. Optional analysis suggests a category, priority, summary, causes and diagnostic actions.

## Architecture

| Project | Responsibility |
| --- | --- |
| Domain | Ticket state, field constraints and timestamp rules |
| Application | Use cases, request validation, DTOs and persistence/analysis interfaces |
| Infrastructure | EF Core persistence and HTTP adapters for Ollama and LiteLLM |
| Api | Controllers, configuration, dependency injection and Problem Details |
| Frontend | Standalone Angular components, reactive forms and typed HTTP services |

Dependencies point toward Domain and Application. Application depends on ticket-specific interfaces rather than EF Core or HTTP clients. The analysis providers are interchangeable adapters selected in the composition root. DbContext handles change tracking and transactional writes.

The Angular application groups pages, components and models by feature. Local UI state uses Signals, and HTTP flows use RxJS cancellation. No global state library is required.

Ticket rules are documented in [docs/ticket-rules.md](docs/ticket-rules.md).

## Prerequisites

- .NET 8 SDK and ASP.NET Core 8 runtime.
- Node.js compatible with Angular 22, such as Node 24.15+ or 26.x, and npm.
- SQL Server Express LocalDB on Windows or another SQL Server instance.
- Ollama with a local model for analysis.
- Docker Desktop with Linux containers for the optional LiteLLM proxy or SQL container.

Check the available tools with `dotnet --list-sdks`, `dotnet --list-runtimes`, `node --version` and `npm --version`. Angular CLI is installed by `npm ci`.

## Run locally

From the repository root:

```powershell
dotnet restore AI.SupportDesk.sln
dotnet tool restore
SqlLocalDB start MSSQLLocalDB

$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project src/AI.SupportDesk.Infrastructure --startup-project src/AI.SupportDesk.Api
dotnet run --project src/AI.SupportDesk.Api --launch-profile local
```

The Development settings use Windows authentication to connect to `(localdb)\MSSQLLocalDB`, database `SupportDesk`. Migrations create the database and schema. They are applied explicitly.

The API runs at http://localhost:5080, with Swagger at http://localhost:5080/swagger. In Visual Studio, use the `local` launch profile.

In another terminal:

```powershell
cd frontend/ai-support-desk
npm ci
npm start
```

Open http://localhost:4200. The development server forwards `/api` requests to the backend through `proxy.conf.json`.

### Another SQL Server or Docker

Override `ConnectionStrings__SupportDesk` with the connection string for your server before running migrations and starting the API. Keep credentials in environment variables or User Secrets.

The optional SQL Compose requires a strong `MSSQL_SA_PASSWORD` value in the shell or ignored root `.env`:

```powershell
docker compose -f compose.yaml up -d
docker compose -f compose.yaml logs sqlserver
```

Once SQL Server is ready, use a connection string with `Server=localhost,1433`, your database, user and password. The development container can use `Encrypt=true;TrustServerCertificate=true`. Use trusted certificate validation for remote deployments.

The migration account needs permission to create the database or modify an existing schema.

## Analysis configuration

Select the path in `src/AI.SupportDesk.Api/appsettings.Development.json` and restart the API after changes.

| Setting | Value |
| --- | --- |
| `AIAnalysis:Provider=Ollama` | Native Ollama HTTP adapter |
| `AIAnalysis:Provider=LiteLLM` | LiteLLM proxy adapter |
| `Ollama:BaseUrl` | `http://localhost:11434` |
| `Ollama:Model` | `qwen3:1.7b` |
| `LiteLLM:BaseUrl` | `http://localhost:4000/v1` |
| `LiteLLM:Model` | `supportdesk-analysis` |
| `Ollama:TimeoutSeconds`, `LiteLLM:TimeoutSeconds` | 120 |

The current development settings select LiteLLM. An absent provider setting defaults to Ollama. Invalid selection or missing provider configuration returns 503 when analysis is requested.

Analysis is stored only in page memory. It leaves the ticket and its timestamps unchanged. Only title and description are sent to the selected provider. Text is requested in Portuguese.

### Native Ollama

Start Ollama and prepare the model:

```powershell
ollama --version
ollama list
ollama pull qwen3:1.7b
Invoke-RestMethod http://localhost:11434/api/version
```

Ollama must be running, either through its desktop application or `ollama serve`. If it is outside PATH, the Windows executable is commonly at `$env:LOCALAPPDATA/Programs/Ollama/ollama.exe`.

The adapter uses `POST /api/chat`, JSON Schema in `format`, `stream=false` and temperature 0. It requires structured-output support. The application does not download models.

### LiteLLM through Docker

Start Docker Desktop and keep Ollama running on Windows. Then run:

```powershell
./scripts/Configure-LiteLlm.ps1
docker compose -f compose.litellm.yaml up -d
docker compose -f compose.litellm.yaml ps
```

The setup script synchronizes one proxy key between the ignored `.env`, development User Secrets and `appsettings.Local.json`. It preserves other settings and reuses an existing key.

The local JSON is a Development-only fallback when the API has no configured key. It is excluded from Git, build output and publication. Existing credentials are preserved; environment variables and command-line arguments retain precedence. For other environments, supply `LiteLLM__ApiKey` through your secret configuration.

The proxy listens on `127.0.0.1:4000` and reads `infra/litellm/config.yaml`. The alias `supportdesk-analysis` routes to `ollama_chat/qwen3:1.7b` at `host.docker.internal:11434`.

To test container access to Ollama:

```powershell
docker compose -f compose.litellm.yaml exec -T litellm python -c "import urllib.request; print(urllib.request.urlopen('http://host.docker.internal:11434/api/version', timeout=5).read().decode())"
```

If Ollama's host-local binding prevents access, configure `OLLAMA_HOST=0.0.0.0:11434` in its startup environment and restart it. Limit inbound access to the required local/container network with Windows firewall rules.

After editing the model mapping, restart the proxy:

```powershell
docker compose -f compose.litellm.yaml restart litellm
```

To stop the proxy:

```powershell
docker compose -f compose.litellm.yaml down
```

This Compose project is independent of the SQL service. Its image is pinned by digest. The proxy timeout is 110 seconds, retries are zero, and message-content logging is disabled.

### Optional OpenAI destination

The OpenAI overlay contains placeholders until a model and project API key are configured. Add the entries from `.env.openai.example` to the existing ignored `.env`, preserving `LITELLM_MASTER_KEY`, and replace the placeholders.

```powershell
docker compose -f compose.litellm.yaml -f compose.litellm.openai.yaml up -d --force-recreate
```

The overlay exposes both aliases:

- `supportdesk-analysis` routes to local Qwen.
- `supportdesk-openai` uses `OPENAI_MODEL` and `OPENAI_API_KEY`.

Select `LiteLLM:Model=supportdesk-openai` and restart the API. The backend uses the proxy key; the OpenAI key stays in the proxy container configuration.

The selected model must support Chat Completions, strict JSON Schema and temperature 0, or the request parameters must be adapted and tested. This destination sends the ticket title and description to the external API. Its real integration remains pending.

Return the proxy to the local configuration with:

```powershell
docker compose -f compose.litellm.yaml up -d --force-recreate
```

## Errors and troubleshooting

API failures use Problem Details. Analysis returns 400 for invalid IDs, 404 for missing tickets, 502 for invalid provider responses, 503 for configuration, availability or authentication failures, and 504 for timeout.

A LiteLLM error saying `No api key passed in` means the caller omitted its proxy credential. The API returns the code `analysis_authentication_failed` for rejected proxy authentication. The Development startup log reports `LiteLLMApiKeyConfigured` without printing the key.

If it is False, run the setup script, stop debugging, rebuild and restart the `local` profile. Check explicit `LiteLLM__ApiKey` or command-line overrides if the value remains empty.

## API examples

```powershell
Invoke-RestMethod 'http://localhost:5080/api/tickets?page=1&pageSize=20&status=Open&sortDirection=desc'

$body = @{
    title = 'Printer offline'
    description = 'The office printer stopped responding.'
    category = 'Hardware'
    status = 'Open'
    priority = 'High'
} | ConvertTo-Json

$ticket = Invoke-RestMethod http://localhost:5080/api/tickets -Method Post -ContentType 'application/json' -Body $body
Invoke-RestMethod "http://localhost:5080/api/tickets/$($ticket.id)/analysis" -Method Post
```

PUT accepts the five editable fields. DELETE returns 204. Lists support page, pageSize (1–100), status, priority, category, search and sortDirection (asc/desc). Category is a case-insensitive full match. Search is a case-insensitive substring of title or description.

## Build and test

```powershell
dotnet build AI.SupportDesk.sln
dotnet test AI.SupportDesk.sln

cd frontend/ai-support-desk
npm run build
npm test -- --watch=false
```

The analysis tests use mocked HTTP responses. The SQL lifecycle test requires an explicit test connection and reports a skip when it is absent:

```powershell
$env:SUPPORTDESK_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true'
dotnet test AI.SupportDesk.sln
```

That test creates and removes a uniquely named database on the configured server. Use a development instance with create/drop database permissions.

For manual analysis verification, save a ticket's GET response, request analysis, fetch it again, and compare all fields and timestamps. Repeat with each provider. In the browser, verify loading, retry after failure and cancellation when navigating to another ticket.

## Limits

This is a local PoC without authentication or production deployment configuration. Model suggestions require human review. Structured JSON does not establish factual correctness. Latency and answer quality depend on the model, context and available hardware.

## References

- [Ollama structured outputs](https://docs.ollama.com/capabilities/structured-outputs)
- [LiteLLM Ollama provider](https://docs.litellm.ai/docs/providers/ollama)
- [LiteLLM proxy configuration](https://docs.litellm.ai/docs/proxy/configs)
- [OpenAI API credentials](https://developers.openai.com/api/docs/guides/api-keys)
