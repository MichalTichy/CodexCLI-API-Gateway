# Codex CLI API Gateway

An internal .NET gateway that exposes a dedicated Codex CLI identity through an OpenAI-compatible HTTP API. It uses [FastEndpoints](https://fast-endpoints.com/), includes a Blazor Web App management UI with Interactive Server rendering, and executes every model run in a short-lived Docker container.

## What it provides

- OpenAI-compatible `chat/completions`, non-streaming JSON-schema output, model listing, versioned MCP tool discovery, files, Bearer authentication, and SSE streaming.
- Projectless runs with disposable workspaces.
- Project runs with persistent artifacts and one run at a time per project.
- Dynamic Codex model and reasoning-effort discovery through Codex App Server.
- Gateway-wide API keys stored in PostgreSQL, with per-project access and separate visible/enabled MCP tool grants for each key.
- A trusted MCP catalog. An administrator assigns each allowed key exact tool visibility and invocation allowlists inside a project.
- A server-side interactive Blazor administration UI for Codex device authentication, projects, API-key grants, and the trusted MCP catalog.
- Bounded concurrency, queueing, timeouts, client-disconnect cancellation, and forced container cleanup.
- One hardened, labelled container per run; there is no host-process model-execution fallback.
- Explicit child-process environment filtering, no-follow artifact handling, and live configurable artifact quotas.
- OpenTelemetry/service discovery defaults and an Aspire AppHost.
- PostgreSQL document persistence through Marten for API keys, projects, grants, and the trusted MCP catalog.
- Deterministic end-to-end tests using real Marten/PostgreSQL plus a fake container engine and fake Codex protocol process; no OpenAI account or model network call is required.

## High-level concept

The gateway has one executable host and several self-contained modules. The host discovers each module's installer and composes them, but business behavior stays in protocol-neutral use cases. API adapters translate an external wire format into those use cases. Infrastructure modules implement the ports needed for PostgreSQL, filesystem storage, Codex, containers, and MCP. Persisted records and shared repository contracts sit at the center and do not depend on an API or infrastructure implementation.

```mermaid
flowchart LR
    Client["Client"] --> Adapter["API adapter<br/>OpenAI today"]
    Admin["Blazor admin UI"] --> UseCases["Logic<br/>use cases + specifications"]
    Adapter --> UseCases
    UseCases --> Models["Models<br/>persisted records"]
    UseCases --> Ports["Logic ports"]
    Ports --> Persistence["Marten / PostgreSQL"]
    Ports --> Storage["Filesystem storage"]
    Ports --> Codex["Codex + containers"]
    Ports --> Mcp["Gateway-hosted MCP"]
    App["App composition root"] -. discovers and registers .-> Adapter
    App -. discovers and registers .-> UseCases
    App -. discovers and registers .-> Persistence
    App -. discovers and registers .-> Storage
    App -. discovers and registers .-> Codex
    App -. discovers and registers .-> Mcp
```

The dependency rule is inward: `App` may reference every selected edge module; an API adapter may reference `Api`, `Logic`, and `Models`; infrastructure implements interfaces owned by `Logic`; and `Logic` depends only on `Models` plus the shared repository/specification contracts. This keeps the OpenAI schema replaceable and prevents database, container, or filesystem details from leaking into use cases.

The normal path through the code is:

1. [`Program.cs`](src/CodexGateway.App/Program.cs) discovers module installers, then defines the visible middleware and endpoint order.
2. An API adapter endpoint, such as [`ChatCompletionsEndpoint.cs`](src/CodexGateway.Api.OpenAI/ChatCompletions/Endpoints/ChatCompletionsEndpoint.cs), validates the wire request and sends a named MediatR use case.
3. A handler in [`Logic/UseCases`](src/CodexGateway.Logic/UseCases/) owns the application operation. Reusable database query intent is expressed by a specification in [`Logic/Specifications`](src/CodexGateway.Logic/Specifications/).
4. The handler accesses persistence through the shared repository contracts and non-database capabilities through ports in `Logic`, such as [`ICodexRunner`](src/CodexGateway.Logic/Codex/ICodexRunner.cs) or [`IProjectStorageManager`](src/CodexGateway.Logic/Storage/IProjectStorageManager.cs).
5. A focused infrastructure module implements each port. Its installer registers the implementation without making the use case depend on that module.

The [component map](#component-map) identifies the owner of every major responsibility and the exact files to start from when changing it.

## Runtime request flow

```mermaid
flowchart LR
    Client["OpenAI client"] -->|"Bearer API key"| API["FastEndpoints API"]
    API --> Scope{"Project selector?"}
    Scope -->|"No selector"| Temp["Disposable workspace"]
    Scope -->|"/p/{id}/v1 or OpenAI-Project"| Grant["Project grant for this API key"]
    Grant --> Project["Locked project workspace"]
    Project --> Artifacts["Persistent artifacts"]
    Grant --> Selection["Key-specific MCP tool allowlist"]
    Temp --> Snapshot["Run workspace snapshot"]
    Artifacts --> Snapshot
    Selection --> Container["Short-lived runner container"]
    Snapshot -->|"mount only /workspace"| Container
    Auth["Dedicated CODEX_HOME"] -->|"mount /codex-home"| Container
    Container --> Run["codex exec --ephemeral"]
    Run -->|"JSONL events"| Adapter["OpenAI response adapter"]
    Adapter --> Client
    Control["Admin UI"] --> Projects["Projects + API-key grants + trusted MCP catalog"]
    Control --> AppServer["Codex App Server"]
    AppServer --> Auth
    AppServer --> Models["Models + reasoning levels"]
```

Every request starts exactly one sibling container from the pinned `codex-gateway-runner:0.148.0` image unless its project selects a trusted runner-image override in the admin UI. A project run first copies persistent artifacts into a private run snapshot. Only that snapshot is mounted at `/workspace`; the live project artifact directory is never mounted. A successful run is checked and atomically committed, while failed, cancelled, and timed-out runs are discarded.

The outer container has a read-only root filesystem, no Linux capabilities, `no-new-privileges`, CPU/memory/PID limits, a bounded tmpfs, a non-root UID, Docker init for descendant reaping, and two labelled mounts. The inner Bubblewrap sandbox gives model-generated commands a private PID view, so `/proc/1/root` cannot be used to reach the outer auth mount.

The dedicated auth volume is mounted at `/codex-home` because the Codex process needs it, but a named inner Codex permission profile grants model-generated commands only minimal runtime reads plus write access to `/workspace` and the bounded `/tmp` tmpfs. Those commands cannot read `/codex-home`; direct command networking is also disabled by the inner sandbox. The container retains its configured network for Codex model/auth traffic and HTTP MCP servers. User configuration/rules/apps/plugins are ignored, and only the MCP servers and tools granted to the authenticated API key in the selected project are injected.

The container receives a small runtime environment allowlist. Gateway-hosted MCP servers receive only a short-lived, run-scoped token that authorizes their internal gateway endpoint; their upstream HTTP credentials and STDIO environment secrets remain in the gateway process. Runner-hosted MCP remains an explicit advanced mode and receives only its configured MCP environment variables. Gateway/API/admin/provider credentials are never inherited, and real `codex exec` runs apply a separate safe-core allowlist to model-generated commands based on the official [shell environment policy](https://learn.chatgpt.com/docs/config-file/config-advanced#shell-environment-policy).

The gateway persists an identity beside its storage and labels containers with `com.codex-gateway.managed=true`, `com.codex-gateway.instance-id`, and `com.codex-gateway.run-id`. At startup it reconciles only stale containers belonging to that storage identity, then safely removes their abandoned run snapshots. Success, failure, cancellation, timeout, and quota enforcement force-remove the matching run container; an abrupt gateway or host crash is recovered on the next startup.

A PostgreSQL database plus storage/auth volume pair supports one live gateway instance. Project serialization and admission locks are in memory, so multiple gateway processes sharing them are unsupported. High availability would require distributed coordination and a different orphan-lease design.

This is strong workspace/process isolation for a trusted internal tool, not a hostile multi-tenant security boundary. Access to the Docker socket is effectively host-administrator access: compromise of the gateway control plane can control the Docker host. Keep the gateway private, use a dedicated Docker host where practical, and never expose the socket to untrusted code. Runner containers do not receive the socket.

## Prerequisites

- [.NET SDK 10.0.301 or newer 10.0 feature band](https://dotnet.microsoft.com/download)
- A current Docker Engine and CLI with `--mount volume-subpath` support (named-volume deployments require it)
- PostgreSQL 18 (started automatically by Aspire or Compose)
- Codex CLI `0.148.0` on the gateway host only when running through Aspire/directly; it is used for App Server model discovery and device login, never for model execution

The gateway uses one dedicated `CODEX_HOME`; do not point it at a developer's normal Codex directory.
Run the gateway as a non-root user so run and auth directories retain the runner UID/GID. The gateway fails startup as root instead of silently creating storage that its non-root runner cannot use. Compose already configures an explicitly non-root gateway user.

The restricted split-filesystem profile requires Codex CLI `0.138` or newer; both shipped images pin `0.148.0`. The runner is Linux-only and uses Bubblewrap inside Docker. Its outer container deliberately sets `seccomp=unconfined` because Docker's default profile blocks Bubblewrap's unprivileged user namespace; the inner permission profile still restricts filesystem reads/writes and command networking.

## Quick start with Aspire

Restore the pinned local Aspire CLI and run the AppHost:

```powershell
dotnet tool restore
dotnet restore
dotnet aspire run --project src/CodexGateway.AppHost/CodexGateway.AppHost.csproj
```

Aspire starts PostgreSQL, builds/tags the `runner` Dockerfile target, then runs the gateway project on the host in bind-mount mode. PostgreSQL holds gateway configuration; `.aspire/data` holds files/artifacts and `.aspire/codex-home` holds Codex authentication.

If `codex` is not the runnable standalone executable on your `PATH`, set an absolute path before starting Aspire:

```powershell
$env:Codex__ExecutablePath = 'C:\path\to\codex.exe'
dotnet aspire run --project src/CodexGateway.AppHost/CodexGateway.AppHost.csproj
```

You can also build the mandatory runner and run the API directly:

```powershell
docker build --target runner -t codex-gateway-runner:0.148.0 .
$env:ConnectionStrings__Gateway = 'Host=localhost;Port=5432;Database=codex_gateway;Username=postgres;Password=postgres'
dotnet run --project src/CodexGateway.App/CodexGateway.App.csproj --urls http://localhost:5050
```

Open `http://localhost:5050/admin`, sign in with the `AdminUi` credentials, create an API key, copy its generated secret when it is shown, then use **Start device login** to authenticate the dedicated Codex identity. The UI uses Blazor Interactive Server and therefore needs its SignalR/WebSocket connection to remain open.

## Docker

Set the required admin credentials and start the gateway:

```powershell
$env:GATEWAY_ADMIN_USERNAME = 'admin'
$env:GATEWAY_ADMIN_PASSWORD = 'a-different-admin-password'
$env:GATEWAY_DB_PASSWORD = 'a-different-database-password'
docker compose up --build
```

The API is at `http://localhost:5050`; the UI is at `http://localhost:5050/admin`. Compose runs PostgreSQL and the gateway, and builds both Dockerfile targets. Its `runner-image` helper exits after ensuring `codex-gateway-runner:0.148.0` exists. The fixed database, data, and auth volumes survive container replacement. If a reverse proxy is placed in front of the gateway, enable WebSocket forwarding for the Blazor circuit.

The gateway stays non-root and receives the Docker socket through a supplementary group. Docker Desktop commonly uses group `0`; on Linux set `DOCKER_GID` to the socket group before starting Compose:

```bash
export DOCKER_GID="$(stat -c '%g' /var/run/docker.sock)"
```

Each sibling runner—not the gateway—sets `seccomp=unconfined` so Bubblewrap can create an unprivileged user namespace. The runner network defaults to `bridge`: Codex authentication, model calls, and HTTP MCP require outbound connectivity, while the inner sandbox blocks direct networking from model-generated shell commands.

Build directly when Compose is not desired:

```powershell
docker build --target runner -t codex-gateway-runner:0.148.0 .
docker build --target gateway -t codex-gateway:latest .
docker volume create codex-gateway-data
docker volume create codex-gateway-auth
docker run --rm -p 5050:8080 `
  --group-add 0 `
  --security-opt no-new-privileges `
  -e AdminUi__Username=admin `
  -e AdminUi__Password=a-different-admin-password `
  -e ConnectionStrings__Gateway='Host=database-host;Port=5432;Database=codex_gateway;Username=codex_gateway;Password=database-password' `
  -e   Codex__Container__Image=codex-gateway-runner:0.148.0 `
  -e Codex__Container__WorkspaceVolume=codex-gateway-data `
  -e Codex__Container__AuthVolume=codex-gateway-auth `
  -v codex-gateway-data:/app/data `
  -v codex-gateway-auth:/app/.codex-home `
  -v /var/run/docker.sock:/var/run/docker.sock `
  codex-gateway:latest
```

Replace `--group-add 0` with the Docker socket GID on a native Linux host. Do not mount the socket into the runner image.

## OpenAI client configuration

Use the same API-key convention as OpenAI:

```http
Authorization: Bearer a-long-internal-api-key
```

API keys are global identities. A project explicitly allows selected key IDs and assigns each allowed key its own visible and enabled MCP tool subsets. Select the project either in the base URL or with OpenAI's standard [`OpenAI-Project`](https://developers.openai.com/api/reference/overview#authentication) header:

| Scope | OpenAI base URL | Additional header | Behavior |
|---|---|---|---|
| Projectless | `http://localhost:5050/v1` | none | Files expire; every run workspace is deleted |
| Project via URL | `http://localhost:5050/p/accounting/v1` | none | Requires that the key is allowed by `accounting` |
| Project via header | `http://localhost:5050/v1` | `OpenAI-Project: accounting` | Same scope and authorization as the project URL |

If the URL and header both select a project, they must match. Any tool that only accepts an OpenAI base URL and key can use the project URL; clients that support OpenAI project selection can keep `/v1` as the base URL and send the header.

### Supported OpenAI-compatible subset

This gateway intentionally implements a tested subset of the [OpenAI Chat Completions API](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create), not the entire OpenAI platform. The supported higher-level integration is the official OpenAI .NET Chat Completions client, optionally adapted to `Microsoft.Extensions.AI.IChatClient` for Microsoft Agent Framework. Configure the client with the project-scoped base URL and select its Chat Completions adapter explicitly; `/v1/responses` is not implemented.

| Area | Supported contract |
|---|---|
| Chat request | `model`, `messages`, `stream`, `reasoning_effort`, Gateway `file_ids`, and non-streaming `response_format` as described below |
| Messages | `system`, `developer`, `user`, and `assistant` text history; optional validated `name`; string content or arrays containing text and Gateway file references |
| Chat response | Standard non-streaming `chat.completion` envelope, or OpenAI-shaped SSE `chat.completion.chunk` events |
| Discovery | Model listing plus the custom rich, versioned MCP catalog route; an OpenAI client does not call the custom tool catalog automatically |
| Files | The listed upload, metadata, content, list, and delete routes |
| Conversation state | Stateless: the caller resends relevant history and every request starts an independent `codex exec --ephemeral` run |

An optional message `name` is accepted for client wire compatibility when it matches `[A-Za-z0-9_-]{1,64}`; the gateway does not otherwise interpret it. Message history is flattened into the new Codex prompt for each independent run.

Gateway-managed MCP tools remain internal to Codex. They are not OpenAI request-level function tools and are not returned as assistant `tool_calls`. Agent Framework consumers must not register local `AITool` functions on this client, and Harness features that emit hosted web search must be disabled. Planning clients retrieve `/tools` separately, while the runtime still enforces the key's enabled-tool grant at the Codex process boundary.

Compatibility is defined by the documented wire subset and the exact package versions tested by the consumer. Pin the OpenAI .NET, `Microsoft.Extensions.AI.OpenAI`, and Microsoft Agent Framework packages; treat an upgrade as a wire-contract change and review captured request fixtures before deploying it.

List the models visible to the gateway's Codex identity:

```powershell
curl.exe http://localhost:5050/v1/models `
  -H "Authorization: Bearer a-long-internal-api-key"
```

Tool discovery supports `GET /v1/tools` and `GET /p/{projectId}/v1/tools`. It always returns the rich, versioned OpenAI-style list envelope for the authenticated key and selected project:

```powershell
curl.exe http://localhost:5050/p/accounting/v1/tools `
  -H "Authorization: Bearer a-long-internal-api-key"
```

The equivalent header-selected request uses `http://localhost:5050/v1/tools` and adds `OpenAI-Project: accounting`. `?detail=full` is accepted as an equivalent alias for clients already configured with that URL:

```powershell
curl.exe "http://localhost:5050/p/accounting/v1/tools?detail=full" `
  -H "Authorization: Bearer a-long-internal-api-key"
```

If `detail` is supplied, `full` is the only accepted value; repeated or different values return `400 invalid_detail`. Servers and tools are ordered deterministically.

```json
{
  "object": "list",
  "catalog_version": "sha256:<opaque-deterministic-hash>",
  "data": [
    {
      "id": "accounting-docs",
      "name": "Accounting documents",
      "version": "2.4.0",
      "required": true,
      "title": "Accounting MCP",
      "description": "Searches approved accounting documents.",
      "website_url": "https://mcp.example.test/accounting",
      "icons": [
        {
          "src": "https://mcp.example.test/icon.png",
          "mimeType": "image/png"
        }
      ],
      "tools": [
        {
          "id": "accounting-docs/open",
          "name": "open",
          "description": "Opens one accounting document.",
          "input_schema": {
            "type": "object",
            "properties": {
              "document_id": { "type": "string" }
            },
            "required": ["document_id"]
          }
        },
        {
          "id": "accounting-docs/search",
          "name": "search",
          "title": "Search documents",
          "description": "Finds accounting documents matching a query.",
          "input_schema": {
            "type": "object",
            "properties": {
              "query": { "type": "string", "minLength": 1 }
            },
            "required": ["query"],
            "additionalProperties": false
          },
          "output_schema": {
            "type": "object",
            "properties": {
              "document_ids": {
                "type": "array",
                "items": { "type": "string" }
              }
            },
            "required": ["document_ids"]
          },
          "annotations": {
            "readOnlyHint": true
          },
          "icons": [
            {
              "src": "data:image/png;base64,...",
              "mimeType": "image/png"
            }
          ],
          "_meta": {
            "com.example/render": { "template": "document-results" }
          }
        }
      ]
    }
  ]
}
```

In the detailed view, `catalog_version` is an opaque deterministic hash of the normalized detailed catalog. Persist it with a plan and retrieve the catalog again before execution; a changed value means the plan should be revalidated. Server entries contain catalog `id` and display `name`, the live MCP implementation `version`, the key-specific grant's `required` flag, and any server-provided `title`, `description`, `website_url`, and `icons`. The pinned Codex App Server status surface does not provide [MCP initialization `instructions`](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization), so the gateway cannot include that field.

Each detailed tool has the stable qualified identity `server-id/tool-name` because names can collide across servers. It follows the official [MCP `Tool` definition](https://modelcontextprotocol.io/specification/2025-11-25/server/tools): `id`, `name`, and `input_schema` are always present, while `title`, `description`, `output_schema`, `annotations`, `icons`, and `_meta` are included when the server supplies them. The input and output schemas, annotations, icons, and `_meta` values retain their complete JSON structures, including vendor extensions. Optional fields are omitted rather than returned as `null`.

The catalog contains only the exact tools enabled for that API key and project. Every returned tool can be used by the caller; tools advertised by an MCP server but absent from the grant are filtered out.

`GET /v1/tools` without a project selector returns the same versioned envelope with empty `data` and a deterministic empty `catalog_version`; it does not contact an MCP server.

Live discovery runs in a short-lived container with the same hardened outer container boundary used for model runs. Its empty workspace is mounted read-only, project artifacts are not copied into it, and a writable ephemeral tmpfs replaces the real Codex authentication directory. Gateway transport URLs, STDIO commands or arguments, forwarded environment-variable names, credentials, and other connection configuration are never added to the response. Before returning server-provided metadata, the gateway also fails closed with a sanitized `503` if it detects a forwarded secret value in literal or common encoded form. A required server that cannot initialize fails the request; an unavailable optional server is omitted.

Descriptions, schemas, annotations, icons, and `_meta` originate from the MCP server and can contain arbitrary server-provided data, URLs, or vendor extensions. Consumers must treat them as untrusted content, validate schemas before use, and sanitize or restrict externally loaded content such as icon URLs and SVGs even when the server is in the gateway's trusted catalog. Secret scanning is defense in depth, not protection against a malicious trusted server that deliberately splits, encrypts, or otherwise transforms a credential before returning it.

Create a completion:

```powershell
curl.exe http://localhost:5050/p/accounting/v1/chat/completions `
  -H "Authorization: Bearer a-long-internal-api-key" `
  -H "Content-Type: application/json" `
  -d '{"model":"gpt-5.6-sol","reasoning_effort":"high","messages":[{"role":"user","content":"Summarize the files in ./artifacts"}]}'
```

The equivalent header-selected request uses `http://localhost:5050/v1/chat/completions` and adds `-H "OpenAI-Project: accounting"`.

For reliable machine-readable output, non-streaming chat also accepts the standard [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs) `response_format` shape:

```json
{
  "model": "gpt-5.6-sol",
  "messages": [
    { "role": "user", "content": "Classify this input." }
  ],
  "response_format": {
    "type": "json_schema",
    "json_schema": {
      "name": "intake_decision",
      "description": "The handling decision for one input.",
      "strict": true,
      "schema": {
        "type": "object",
        "properties": {
          "decision": {
            "type": "string",
            "enum": ["discard", "create", "needs_review"]
          },
          "reason": { "type": "string" }
        },
        "required": ["decision", "reason"],
        "additionalProperties": false
      }
    }
  }
}
```

`response_format.type` may be `text` or `json_schema`. JSON-schema mode requires a 1-64 character alphanumeric, underscore, or hyphen `name`, an object-valued `schema`, and non-streaming execution. `strict` may be omitted or `true`; `false` is rejected. The optional description is limited to 1,024 characters.

Schemas are limited to 64 KiB of UTF-8 JSON, 32 levels, 10,000 JSON nodes, 1,000 JSON properties, 256-character property names, and 16,384-character string values. Local fragment `$ref` values are allowed; remote references and `$id`, `$dynamicRef`, and `$recursiveRef` are rejected. Invalid and unsupported schemas return an OpenAI-shaped `400` and are never silently downgraded to text.

The gateway stages the validated schema at the private run-only `.gateway/output-schema.json` path and invokes Codex with `--output-schema`; it is not committed to project artifacts. A successful result must also parse as JSON. The normal Chat Completions envelope is unchanged, with the structured JSON serialized in `choices[0].message.content`; invalid final JSON produces a sanitized `502` instead of a successful response.

`/v1/models` includes the extension fields `supported_reasoning_efforts` and `default_reasoning_effort`. A chat request must choose one listed model; `reasoning_effort` is optional and defaults to the model's reported default.

`stream: true` returns valid OpenAI SSE chunks and terminates with `data: [DONE]`. Codex `exec --json` currently guarantees event-level JSONL rather than token deltas, so the content chunk is emitted when Codex completes an assistant message. `stream_options.include_usage: true` adds the standard final usage-only chunk before `[DONE]`.

### Explicitly unsupported chat features

Semantics-changing OpenAI fields are rejected instead of being silently ignored:

| Request field | Accepted compatibility value | Rejected behavior |
|---|---|---|
| `tools` | missing, `null`, or `[]` | A non-empty function-tool array |
| `functions` | missing, `null`, or `[]` | A non-empty legacy function array |
| `tool_choice` | missing, `null`, or `"none"` | `auto`, `required`, or a named function choice |
| `function_call` | missing, `null`, or `"none"` | `auto` or a named legacy function choice |
| `parallel_tool_calls` | missing, `null`, or `false` | `true` |
| `web_search_options` | missing or `null` | Any non-null value; use Gateway-managed MCP search instead |
| `messages[].tool_calls` | missing, `null`, or `[]` | Non-empty assistant tool calls |
| `messages[].function_call` | missing or `null` | Any legacy assistant function call |
| `messages[].role` | `system`, `developer`, `user`, or `assistant` | `tool` and every other unsupported role |
| `stream_options.include_usage` | missing, `null`, `false`, or `true` | No rejected V1 value |
| `response_format.type` | `text`, or non-streaming `json_schema` | `json_object`, other format types, or JSON-schema streaming |

Unsupported function/tool/search/stream options and response-format types return `400 invalid_request_error` with `code: "unsupported_parameter"` and the offending field in `error.param`. An unknown message role uses `unsupported_message_role`; malformed values use the normal invalid-request or schema-specific code.

The Responses API, OpenAI function-calling turns, tool-call streaming deltas, hosted web search, images, audio, log probabilities, multiple choices, and arbitrary sampling controls are outside this compatibility contract. Do not depend on an undocumented OpenAI field merely because JSON deserialization happens to tolerate it.

## Files

Supported routes exist under both base URL forms:

```text
POST   /v1/files
GET    /v1/files
GET    /v1/files/{file_id}
GET    /v1/files/{file_id}/content
DELETE /v1/files/{file_id}
```

Upload using the OpenAI multipart shape:

```powershell
curl.exe http://localhost:5050/p/accounting/v1/files `
  -H "Authorization: Bearer a-long-internal-api-key" `
  -F "purpose=assistants" `
  -F "file=@report.csv"
```

Project uploads are persistent artifacts shared by the keys allowed into that project. Projectless uploads are private to the API-key ID that uploaded them, expire after `Gateway:Files:ProjectlessTtlHours`, and must be referenced using either the request-level `file_ids` array or the standard nested content part `{"type":"file","file":{"file_id":"file_..."}}`. The top-level `file_id` form is also accepted as a compatibility extension.

Uploaded project files live in the writable artifact tree. If Codex modifies one, file metadata and download length are refreshed from the committed content; if Codex deletes or renames it, the old file ID becomes unavailable.

Project uploads share the project's artifact file-count, per-file, and aggregate-size limits. The gateway samples the entire writable run workspace—including `artifacts`, `.tmp`, and the isolated shell home—every 100 ms while the run container is active, then performs a mandatory final scan. Exceeding any limit force-removes the container and returns `artifact_limit_exceeded` instead of waiting for artifact commit. This prevents commands from evading the storage guard by writing outside `artifacts`. Reparse points are never traversed by the monitor, and special files are probed with non-following/nonblocking handles.

The sampled guard can briefly overshoot before its next poll; it measures logical file bytes and file-like entries rather than allocated blocks or empty-directory inode use. The limits are per project or per run, not a global disk quota. The number of projects is not capped, and projectless uploads are bounded per file and removed by TTL rather than by a hard aggregate-storage ceiling. Put `Gateway:StoragePath` on a dedicated volume with an operating-system/storage quota when a hard installation-wide ceiling is required.

## Projects and MCP servers

Projects are created only through the management UI. Project IDs are immutable. A project request succeeds only when its API key has an explicit access entry; unknown, disabled, or inaccessible projects use the same `401 invalid_api_key` response so the public API does not reveal project existence.

The MCP catalog is the trust boundary:

1. Add a trusted HTTP or STDIO server in the admin UI.
2. Declare the tools that server is allowed to expose.
3. In a project, allow an API key and assign that key the server plus exact visible and enabled tools.
4. Repeat independently for other keys that need different capabilities in the same project.
5. A run resolves the authenticated key's assignment and receives only its enabled subset through Codex `enabled_tools` configuration.

Read and write tools are both supported. Enabled tools are pre-approved because API runs cannot answer interactive prompts, and every enabled tool must also be visible. In the management UI, **Plan** controls visibility and **Invoke** controls execution; selecting Invoke automatically selects Plan. Newly added server tools remain invisible and disabled for every key until an administrator explicitly selects them. API callers cannot override visibility or invocation permissions.

The UI can globally disable a catalog server and choose where it executes. **Gateway** is the default for new servers: the runner is configured with a temporary internal MCP URL and scoped bearer token, while the gateway adds configured environment-backed HTTP headers or starts the configured local STDIO command. The internal endpoint fixes the configured upstream server and rejects missing, expired, or incorrect tokens. An HTTP mapping such as `X-Api-Key=MCP_API_KEY` sends the environment variable's complete value in that header; `Authorization=MCP_AUTHORIZATION` supports Bearer, Basic, or another authorization scheme without storing the secret in gateway state. Browser/OAuth authorization is not supported.

Gateway-hosted STDIO commands execute as local gateway child processes, with the run workspace as working directory. They receive only their configured environment variables plus the minimal process environment needed to execute. They are trusted host-level executables: catalog administration must therefore remain limited to the trusted administrator. Their process trees are stopped when the run or tool-discovery session ends.

**Runner** is an advanced compatibility mode for servers that must connect directly from the selected runner image. STDIO executable paths must exist in that image, and runner-hosted servers share the runner's workspace, internet access, and Codex login state. HTTP servers use the same environment-backed header mappings through Codex `env_http_headers`. The default image intentionally contains only Codex, Bubblewrap, Node/npm, certificates, and Ubuntu base utilities; build a derived runner image for any additional STDIO binary.

Remote HTTP header values and STDIO secrets are referenced by environment-variable name and are never placed in process arguments or persisted state. Gateway-hosted values never enter the runner container; runner-hosted values are excluded from model-generated shell environments.

Projectless runs receive no MCP servers.
The tool-discovery routes apply the same project selection and per-key authorization rules as model runs, so callers can inspect the complete live metadata for their exact grants without learning catalog connection configuration or credentials.

## Configuration

All settings can be supplied through `appsettings.json` or normal ASP.NET Core environment variables (`__` separates sections).

| Setting | Default | Meaning |
|---|---:|---|
| `ConnectionStrings:Gateway` | required | PostgreSQL connection used by Marten in every environment |
| `Gateway:StoragePath` | `data` | Projects, metadata, temporary files, and runs |
| `Gateway:Limits:MaxConcurrent` | `4` | Concurrent run containers |
| `Gateway:Limits:MaxQueued` | `32` | Additional admitted requests |
| `Gateway:Limits:TimeoutSeconds` | `1800` | Active execution timeout in seconds; queue wait is excluded |
| `Gateway:Files:MaxUploadMegabytes` | `25` | Upload limit |
| `Gateway:Files:ProjectlessTtlHours` | `24` | Temporary-file lifetime |
| `Gateway:Artifacts:MaxFiles` | `10000` | Maximum files in a project artifact tree or writable run workspace |
| `Gateway:Artifacts:MaxFileMegabytes` | `512` | Maximum file size in project artifacts or the writable run workspace |
| `Gateway:Artifacts:MaxTotalMegabytes` | `2048` | Maximum total project artifacts or writable run-workspace size; project uploads share this budget |
| `Codex:ExecutablePath` | `codex` | Host/control-plane Codex CLI used only for App Server auth and model discovery |
| `Codex:ArgumentPrefix` | empty | Control-plane prefix arguments; primarily used by deterministic tests |
| `Codex:HomePath` | `.codex-home` | Dedicated persistent Codex identity |
| `Codex:Container:EngineExecutablePath` | `docker` | Docker-compatible client used to create/inspect/remove run containers |
| `Codex:Container:EngineArgumentPrefix` | empty | Optional engine prefix, useful for a remote Docker context |
| `Codex:Container:Image` | `codex-gateway-runner:0.148.0` | Mandatory runner image; customize this for packaged STDIO MCP binaries |
| `Codex:Container:WorkspaceVolume` | unset | Named gateway-data volume in containerized deployment; unset selects host bind mounts |
| `Codex:Container:AuthVolume` | unset | Named auth volume in containerized deployment; unset selects the configured host auth path |
| `Codex:Container:Network` | `bridge` | Outer runner network; it must reach `McpGateway:RunnerBaseUrl` for gateway-hosted MCP |
| `Codex:Container:MemoryMegabytes` | `2048` | Per-run outer memory limit |
| `Codex:Container:CpuLimit` | `2` | Per-run CPU limit |
| `Codex:Container:PidsLimit` | `256` | Per-run PID limit |
| `Codex:Container:TmpfsMegabytes` | `256` | Size of the per-run temporary filesystem |
| `Codex:ModelCacheSeconds` | `300` | App Server model-catalog cache |
| `Codex:AppServerRequestTimeoutSeconds` | `30` | Control-plane request timeout |
| `Codex:DeviceLoginTimeoutMinutes` | `15` | Maximum pending device-login maintenance window |
| `Codex:DeviceLoginTimeoutSeconds` | unset | Optional seconds override for short operational limits/tests |
| `McpGateway:RunnerBaseUrl` | `http://host.docker.internal:8080` | Gateway URL reachable from runner containers; Compose configures `http://gateway:8080` on its dedicated bridge network |
| `McpGateway:SessionLifetimeMinutes` | `120` | Maximum lifetime of a scoped internal MCP session |
| `AdminUi:Enabled` | `true` | Enables the management page and login |
| `AdminUi:Username` | `admin` | Single admin username, stored as plain text as requested |
| `AdminUi:Password` | `internal-password` | Single admin password, stored as plain text as requested |

The admin browser cookie is a non-persistent session cookie and expires when the browser session ends. Each login also receives an in-memory server session: logout invalidates copied cookies and prevents further events on existing Blazor circuits, while an operation that already started is allowed to finish. A gateway restart invalidates outstanding admin cookies. There are no roles or multiple gateway users. OpenAI API authentication and admin UI authentication are intentionally separate. The Blazor UI executes management operations directly on the server through its authenticated circuit; there is no separate management JSON API. The only admin transport routes are the antiforgery-protected form posts used to issue and revoke the UI cookie. Known placeholder credentials are rejected outside Development.

API keys are stored only in PostgreSQL and managed through the administration UI. A new gateway starts without API keys: sign in to the UI and create the first key before calling the OpenAI-compatible API. `CreateApiKeyUseCase` generates a cryptographically random 256-bit secret and returns it to the UI, which displays it only after successful creation. The secret is not included in later reads, so copy it immediately. The management UI refers to keys by ID and name when assigning project access and tools.

## Error contract

Errors use the OpenAI shape and include an `x-request-id` response header:

```json
{
  "error": {
    "message": "The gateway run queue is full.",
    "type": "rate_limit_error",
    "param": null,
    "code": "rate_limit_exceeded"
  }
}
```

The gateway maps invalid keys and inaccessible project scopes to `401`, selector mismatches and invalid model/reasoning to `400`, missing files to `404`, oversized uploads or artifact trees to `413`, a full queue to `429`, unavailable Codex/required MCP to `503`, timeouts to `504`, and Codex failures to `502`. Raw stderr, prompts, local paths, and secrets are never returned.

## Tests

Run the complete repeatable suite:

```powershell
dotnet test CodexGateway.slnx
```

The end-to-end tests host the full ASP.NET Core pipeline and call it through `HttpClient`. They run the production Marten repository against a disposable PostgreSQL 18 Testcontainer. A deterministic fake container engine launches `CodexGateway.FakeCodex` for the run JSONL protocol, while the fake also implements the App Server protocol. The suite covers API-key/project grants, URL and `OpenAI-Project` scope selection, API/admin authentication boundaries, Blazor login/dashboard/logout rendering, framework assets and circuit negotiation, models/reasoning, rich versioned tool discovery, visible-versus-enabled grants, explicit unsupported-feature errors, JSON-schema output staging and validation, non-streaming and SSE responses (including usage chunks), nested file inputs and scope isolation, project persistence and parallel/serialized runs, per-key MCP filtering, device auth, queue overflow, cancellation, timeouts, stderr flooding, live quota termination, aggregate project-upload limits, symlink rejection, container failures, orphan reconciliation, hardened argument construction, and information-leak prevention. Tests require Docker, but no Codex login or remote AI call. The first run may pull the pinned PostgreSQL image.

The separate official-client compatibility suite pins `OpenAI` `2.13.0` as the Gateway's reviewed client version and commits sanitized golden wire fixtures for plain/history chat, SSE with usage, JSON-schema output, and rejected local function tools. Samwise is currently scaffolded but does not yet pin OpenAI, `Microsoft.Extensions.AI.OpenAI`, or Microsoft Agent Framework packages, so `2.13.0` must not be described as Samwise's pin. Align this suite and add Agent Framework session round-trip coverage when Samwise commits those exact versions.

Build the production container as an additional packaging check:

```powershell
./scripts/verify-container.ps1
```

That repeatable smoke builds both image targets and verifies:

- the pinned CLI, Bubblewrap, non-root runner, and gateway Docker client;
- a real inner filesystem/network denial under Codex sandbox code;
- the outer read-only filesystem, dropped capabilities, security settings, resource limits, labels, mounts, configured network, writable bounded tmpfs, and Bubblewrap PID-view denial of the outer auth mount;
- gateway health, the admin page, API authentication, access to the Docker engine, cleanup, and absence of a stale verification-labelled container.

It deliberately does not make a paid/live model request because a repeatable packaging check has no Codex credentials. Use `-SkipBuild -GatewayImage codex-gateway:verify -RunnerImage codex-gateway-runner:0.148.0` to retest existing images.

## Component map

Use this section as the entry point for code changes. Each component owns one kind of decision; follow the links in the last column before searching the rest of the repository.

### Composition and module boundaries

| Component | Responsibility | Start here and change here |
|---|---|---|
| Application host | Builds the process, discovers every selected module, and keeps security-sensitive middleware and endpoint order visible. It is the only project that composes API adapters with infrastructure. | Start at [`Program.cs`](src/CodexGateway.App/Program.cs). Add or reorder middleware and mapped endpoint groups there. Add a new module to the process through [`CodexGateway.App.csproj`](src/CodexGateway.App/CodexGateway.App.csproj), then let installer discovery register it. |
| Installer convention | Gives each module one self-contained place for dependency registration. Installers run deterministically in high, normal, then low priority order. | The contracts and discovery algorithm are in [`Shared.Infrastructure.IoC/Installers`](SharedInfrastructure/Shared.Infrastructure.IoC/Installers/). Change a module's registrations in its own `Composition/*Installer.cs`; for example [`LogicInstaller.cs`](src/CodexGateway.Logic/Composition/LogicInstaller.cs). Do not hide middleware order in an installer. |
| Architecture rules | Enforces inward dependencies, adapter isolation, installer ownership, and the one-top-level-type-per-file convention. | Change architectural policy in [`ArchitectureDependencyTests.cs`](tests/CodexGateway.Tests/Architecture/ArchitectureDependencyTests.cs), [`InstallerDiscoveryTests.cs`](tests/CodexGateway.Tests/Architecture/InstallerDiscoveryTests.cs), and [`SourceFileArchitectureTests.cs`](tests/CodexGateway.Tests/Architecture/SourceFileArchitectureTests.cs). Update these tests intentionally when introducing a new allowed dependency. |

`App` references the selected edge modules. API adapters reference the schema-neutral `Api` project and the inward projects they need. Infrastructure projects implement interfaces owned by `Logic`. `Logic` references `Models` and shared abstractions, but not Marten, FastEndpoints, Blazor, Docker, or a specific API schema.

### Core application and persistence model

| Component | Responsibility | Start here and change here |
|---|---|---|
| Persisted gateway model | Defines the durable API keys, projects, project grants, MCP catalog, and file records. [`GatewayState`](src/CodexGateway.Models/Gateway/GatewayState.cs) is stored as one optimistic-concurrency Marten document so related configuration changes are atomic. | Change the aggregate in [`CodexGateway.Models/Gateway`](src/CodexGateway.Models/Gateway/) and the relevant record folder: [`ApiKeys`](src/CodexGateway.Models/ApiKeys/), [`Projects`](src/CodexGateway.Models/Projects/), [`McpServers`](src/CodexGateway.Models/McpServers/), or [`Files`](src/CodexGateway.Models/Files/). Then update affected use cases, specifications, UI editors, and tests. |
| Use cases | Own one named application operation each: authenticate a request, manage a project or API key, list models, discover tools, handle a file, or generate an assistant response. Endpoints and Blazor components call these handlers through MediatR. | Start in [`CodexGateway.Logic/UseCases`](src/CodexGateway.Logic/UseCases/), organized by feature. Add behavior to the relevant handler instead of an endpoint or generic service. The model-run entry point is [`GenerateAssistantResponseUseCase.cs`](src/CodexGateway.Logic/UseCases/Generation/GenerateAssistantResponseUseCase.cs); request authorization starts in [`AuthenticateGatewayRequestUseCase.cs`](src/CodexGateway.Logic/UseCases/Security/AuthenticateGatewayRequestUseCase.cs). |
| Specifications | Describe reusable database query intent and keep filtering and ordering translatable to Marten/PostgreSQL. They are for persisted queries, not filesystem, process, or HTTP operations. | Change project, API-key, or MCP query rules in [`CodexGateway.Logic/Specifications`](src/CodexGateway.Logic/Specifications/). Put ordering in the specification's `Apply` method so it executes in the database. Translation coverage lives in [`MartenProjectionTranslationTests.cs`](tests/CodexGateway.Tests/Architecture/MartenProjectionTranslationTests.cs). |
| Application ports | Prevent application behavior from depending on concrete filesystem, Codex, container, or MCP implementations. | Codex ports are in [`Logic/Codex`](src/CodexGateway.Logic/Codex/), storage ports in [`Logic/Storage`](src/CodexGateway.Logic/Storage/), and the gateway MCP session port in [`Logic/McpServers`](src/CodexGateway.Logic/McpServers/). Change an interface only when the application needs a new capability, then update its owning infrastructure adapter. |
| Generation orchestration | Converts normalized messages into a Codex prompt, controls admission/project serialization, prepares MCP access, runs Codex, and returns a protocol-neutral result. | Change normalized request/result shapes in [`Logic/Generation/Models`](src/CodexGateway.Logic/Generation/Models/), prompt construction in [`PromptComposer.cs`](src/CodexGateway.Logic/Generation/PromptComposer.cs), concurrency in [`RunCoordinator.cs`](src/CodexGateway.Logic/Codex/RunCoordinator.cs), and end-to-end orchestration in [`GenerateAssistantResponseUseCase.cs`](src/CodexGateway.Logic/UseCases/Generation/GenerateAssistantResponseUseCase.cs). |
| Repository abstractions | Provide the Edifio-style repository/specification boundary used by `Logic`. This is vendored shared infrastructure rather than gateway-specific application code. | Repository contracts are in [`Shared.Infrastructure.Persistence/Repositories`](SharedInfrastructure/Shared.Infrastructure.Persistence/Repositories/) and specification contracts in [`Shared.Infrastructure.Persistence/Specifications`](SharedInfrastructure/Shared.Infrastructure.Persistence/Specifications/). Change these only for a capability that should be shared by every persisted aggregate. |
| Marten persistence | Adapts the shared repositories to PostgreSQL, configures optimistic concurrency, and initializes the singleton gateway state. The same implementation is used in every environment. | Gateway registration is in [`PersistenceInfrastructureInstaller.cs`](src/CodexGateway.Infrastructure.Persistence/Composition/PersistenceInfrastructureInstaller.cs) and initialization in [`GatewayStateInitializer.cs`](src/CodexGateway.Infrastructure.Persistence/Initialization/GatewayStateInitializer.cs). Generic Marten behavior is in [`Shared.Infrastructure.Persistence.Marten`](SharedInfrastructure/Shared.Infrastructure.Persistence.Marten/). |

Dashboard reads use `IReadOnlyRepository<GatewayState>` with explicit specifications. Write use cases use `IRepository<GatewayState>` directly. There is deliberately no gateway-specific repository facade or pass-through application-service layer.

### HTTP and API schemas

| Component | Responsibility | Start here and change here |
|---|---|---|
| Schema-neutral API bootstrap | Configures FastEndpoints once, collects only explicitly selected adapter assemblies, sets request-size limits, and exposes the authenticated request context without knowing an external schema. | Change shared HTTP bootstrap in [`GatewayApiInstaller.cs`](src/CodexGateway.Api/Composition/GatewayApiInstaller.cs), endpoint assembly selection in [`GatewayEndpointAssembly.cs`](src/CodexGateway.Api/Composition/GatewayEndpointAssembly.cs), and request-context access in [`GatewayRequestContextExtensions.cs`](src/CodexGateway.Api/Security/GatewayRequestContextExtensions.cs). Schema-specific routes and envelopes do not belong here. |
| OpenAI request pipeline | Recognizes OpenAI and project-scoped paths, extracts the Bearer credential and `OpenAI-Project` selector, authenticates through the security use case, and maps failures to the OpenAI error shape. | Start at [`OpenAiRequestMiddleware.cs`](src/CodexGateway.Api.OpenAI/Pipeline/OpenAiRequestMiddleware.cs). Change OpenAI errors in [`OpenAiErrorResponseMapper.cs`](src/CodexGateway.Api.OpenAI/Errors/OpenAiErrorResponseMapper.cs) and JSON defaults in [`OpenAiJson.cs`](src/CodexGateway.Api.OpenAI/Serialization/OpenAiJson.cs). |
| Chat Completions adapter | Owns the OpenAI chat wire DTOs, validation, mapping, endpoint, and SSE/non-streaming response format. It translates to protocol-neutral generation models and does not execute Codex directly. | Change the route/response stream in [`ChatCompletionsEndpoint.cs`](src/CodexGateway.Api.OpenAI/ChatCompletions/Endpoints/ChatCompletionsEndpoint.cs), accepted request fields in [`ChatCompletionRequest.cs`](src/CodexGateway.Api.OpenAI/ChatCompletions/Models/ChatCompletionRequest.cs), rules in [`ChatCompletionRequestValidator.cs`](src/CodexGateway.Api.OpenAI/ChatCompletions/ChatCompletionRequestValidator.cs), and translation in [`OpenAiChatCompletionMapper.cs`](src/CodexGateway.Api.OpenAI/ChatCompletions/OpenAiChatCompletionMapper.cs). |
| Model, tool, and file endpoints | Expose model discovery, the custom rich tool catalog, and OpenAI-shaped file operations by calling their corresponding use cases. | Models: [`ModelsEndpoint.cs`](src/CodexGateway.Api.OpenAI/ModelCatalog/Endpoints/ModelsEndpoint.cs) and [`ListModelsUseCase.cs`](src/CodexGateway.Logic/UseCases/ModelCatalog/ListModelsUseCase.cs). Tools: [`ToolsEndpoint.cs`](src/CodexGateway.Api.OpenAI/Tools/Endpoints/ToolsEndpoint.cs) and [`GetToolCatalogUseCase.cs`](src/CodexGateway.Logic/UseCases/Tools/GetToolCatalogUseCase.cs). Files: [`Api.OpenAI/Files`](src/CodexGateway.Api.OpenAI/Files/) and [`Logic/UseCases/Files`](src/CodexGateway.Logic/UseCases/Files/). |
| Additional API schema | Encapsulates another external contract without changing normalized application behavior. Each adapter owns its routes, DTOs, validation, authentication extraction, error shape, streaming, mapping, and installer. | Follow [Adding an API adapter](#adding-an-api-adapter). Use [`CodexGateway.Api.OpenAI`](src/CodexGateway.Api.OpenAI/) as the reference implementation, not as a dependency. |

### Infrastructure adapters

| Component | Responsibility | Start here and change here |
|---|---|---|
| File storage | Resolves storage roots, isolates project and projectless files, creates private run snapshots, commits successful artifacts, enforces quotas, and removes expired temporary files. | Registration is in [`FileStorageInfrastructureInstaller.cs`](src/CodexGateway.Infrastructure.FileStorage/Composition/FileStorageInfrastructureInstaller.cs). Change paths in [`StoragePaths.cs`](src/CodexGateway.Infrastructure.FileStorage/Configuration/StoragePaths.cs), persistent projects in [`ProjectStorageManager.cs`](src/CodexGateway.Infrastructure.FileStorage/Projects/ProjectStorageManager.cs), uploaded files in [`FileStore.cs`](src/CodexGateway.Infrastructure.FileStorage/Files/FileStore.cs), run snapshots in [`WorkspaceManager.cs`](src/CodexGateway.Infrastructure.FileStorage/Workspaces/WorkspaceManager.cs), and quota enforcement in [`Artifacts`](src/CodexGateway.Infrastructure.FileStorage/Artifacts/). |
| Codex control plane | Talks to Codex App Server on the host for device login, account state, model discovery, and reasoning-effort discovery. It is separate from model execution. | Change the protocol client and cache behavior in [`CodexAppServerClient.cs`](src/CodexGateway.Infrastructure.Codex/AppServer/CodexAppServerClient.cs). Its application-facing contract is [`ICodexControlPlane.cs`](src/CodexGateway.Logic/Codex/ICodexControlPlane.cs); admin actions are in [`UseCases/CodexAuthentication`](src/CodexGateway.Logic/UseCases/CodexAuthentication/). |
| Container execution | Builds hardened Docker arguments, starts one short-lived runner for each model call, filters its environment, consumes Codex JSONL events, cancels/removes containers, and reconciles stale containers at startup. There is no host execution fallback. | Start with [`ContainerCodexRunner.cs`](src/CodexGateway.Infrastructure.Codex/Containers/ContainerCodexRunner.cs). Change Docker arguments and mounts in [`ContainerCommandBuilder.cs`](src/CodexGateway.Infrastructure.Codex/Containers/ContainerCommandBuilder.cs), lifecycle calls in [`ContainerRuntime.cs`](src/CodexGateway.Infrastructure.Codex/Containers/ContainerRuntime.cs), environment policy in [`CodexProcessEnvironment.cs`](src/CodexGateway.Infrastructure.Codex/Containers/CodexProcessEnvironment.cs), and startup checks/reconciliation in [`ContainerRuntimePreflightInitializer.cs`](src/CodexGateway.Infrastructure.Codex/Containers/ContainerRuntimePreflightInitializer.cs). |
| MCP metadata discovery | Connects to configured trusted servers to obtain live server descriptions, tool descriptions, schemas, annotations, and icons before filtering the catalog for a key/project grant. | Change discovery and metadata normalization in [`McpMetadataDiscoveryService.cs`](src/CodexGateway.Infrastructure.Codex/Mcp/McpMetadataDiscoveryService.cs). Change the returned application model in [`Logic/Tools/Models`](src/CodexGateway.Logic/Tools/Models/) and authorization/filtering in [`GetToolCatalogUseCase.cs`](src/CodexGateway.Logic/UseCases/Tools/GetToolCatalogUseCase.cs). |
| Gateway-hosted MCP | Creates run-scoped sessions with opaque tokens, proxies HTTP servers, launches local STDIO servers, keeps upstream secrets in the gateway, and cleans sessions up. | Registration/options are in [`GatewayMcpInfrastructureInstaller.cs`](src/CodexGateway.Infrastructure.Mcp/Composition/GatewayMcpInfrastructureInstaller.cs) and [`GatewayMcpOptions.cs`](src/CodexGateway.Infrastructure.Mcp/Configuration/Models/GatewayMcpOptions.cs). Change session/token behavior in [`GatewayMcpSessionManager.cs`](src/CodexGateway.Infrastructure.Mcp/Sessions/GatewayMcpSessionManager.cs), transports in [`Transport`](src/CodexGateway.Infrastructure.Mcp/Transport/), and the internal runner-facing route in [`GatewayMcpEndpointExtensions.cs`](src/CodexGateway.App/Mcp/Endpoints/GatewayMcpEndpointExtensions.cs). |

### Management UI, hosting, and tests

| Component | Responsibility | Start here and change here |
|---|---|---|
| Blazor management UI | Provides the trusted server-side interface for Codex login, API keys, projects/grants, and MCP catalog configuration. Components send the same use cases used by other edge adapters; there is no management JSON API. | The route shell is [`AdminPage.razor`](src/CodexGateway.App/Components/Pages/Admin/AdminPage.razor) with code in [`AdminPage.razor.cs`](src/CodexGateway.App/Components/Pages/Admin/AdminPage.razor.cs). Feature UI is grouped under [`ApiKeys`](src/CodexGateway.App/Components/Pages/Admin/ApiKeys/), [`Codex`](src/CodexGateway.App/Components/Pages/Admin/Codex/), [`Dashboard`](src/CodexGateway.App/Components/Pages/Admin/Dashboard/), [`McpServers`](src/CodexGateway.App/Components/Pages/Admin/McpServers/), and [`Projects`](src/CodexGateway.App/Components/Pages/Admin/Projects/). Keep markup in `.razor` and behavior in the matching `.razor.cs`. |
| Admin authentication/session | Owns the single admin cookie, login/logout form routes, server-side session revocation, and Blazor circuit revalidation. This is separate from OpenAI API-key authentication. | Change login/logout routes in [`AdminAuthenticationRoutes.cs`](src/CodexGateway.App/Admin/Authentication/AdminAuthenticationRoutes.cs), settings in [`AdminUiOptions.cs`](src/CodexGateway.App/Admin/Authentication/Models/AdminUiOptions.cs), registration/cookie policy in [`AppInstaller.cs`](src/CodexGateway.App/Composition/AppInstaller.cs), and circuit/session behavior in [`Admin/Sessions`](src/CodexGateway.App/Admin/Sessions/). |
| Configuration | Defines validated option shapes and their deployment values. The option class explains meaning; `appsettings.json`, environment variables, Aspire, and Compose provide values. | Gateway/run/file/artifact settings are in [`Logic/Configuration/Models`](src/CodexGateway.Logic/Configuration/Models/); MCP settings in [`GatewayMcpOptions.cs`](src/CodexGateway.Infrastructure.Mcp/Configuration/Models/GatewayMcpOptions.cs); admin settings in [`AdminUiOptions.cs`](src/CodexGateway.App/Admin/Authentication/Models/AdminUiOptions.cs). Defaults are in [`appsettings.json`](src/CodexGateway.App/appsettings.json), Aspire wiring in [`AppHost/Program.cs`](src/CodexGateway.AppHost/Program.cs), and container deployment in [`compose.yaml`](compose.yaml). Update the [configuration table](#configuration) when adding or renaming a public setting. |
| Service defaults | Adds health endpoints, OpenTelemetry, service discovery, and standard HTTP resilience used by the host. | Change shared operational defaults in [`ServiceDefaults/Hosting/Extensions.cs`](src/CodexGateway.ServiceDefaults/Hosting/Extensions.cs). Map them from [`App/Program.cs`](src/CodexGateway.App/Program.cs). |
| Aspire AppHost | Runs PostgreSQL, builds the runner image, and starts the gateway with local persistent directories for development. | Change local orchestration in [`CodexGateway.AppHost/Program.cs`](src/CodexGateway.AppHost/Program.cs). Production images and isolation are defined by [`Dockerfile`](Dockerfile) and [`compose.yaml`](compose.yaml). |
| Unit and architecture tests | Check use cases, adapters, storage, container argument construction, MCP sessions, and architecture without a live service. | Add focused tests under [`tests/CodexGateway.Tests`](tests/CodexGateway.Tests/), mirroring the production feature folder. |
| OpenAI compatibility tests | Exercise the official pinned OpenAI .NET client and compare sanitized requests/responses with committed golden wire fixtures. | Change compatibility scenarios in [`OfficialOpenAiClientCompatibilityTests.cs`](tests/CodexGateway.OpenAICompatibilityTests/Compatibility/OfficialOpenAiClientCompatibilityTests.cs) and reviewed wire contracts in [`Fixtures`](tests/CodexGateway.OpenAICompatibilityTests/Fixtures/). |
| End-to-end tests and fake Codex | Host the complete ASP.NET pipeline and replace Docker/Codex with deterministic protocol test doubles. | Add behavior scenarios under [`tests/CodexGateway.EndToEndTests`](tests/CodexGateway.EndToEndTests/). Test host substitutions are in [`GatewayFactory.cs`](tests/CodexGateway.EndToEndTests/Infrastructure/GatewayFactory.cs); the fake process starts at [`CodexGateway.FakeCodex/Program.cs`](tests/CodexGateway.FakeCodex/Program.cs). Production-container verification is in [`scripts/verify-container.ps1`](scripts/verify-container.ps1). |
| Shared test database | Starts one PostgreSQL 18 Testcontainer per HTTP test assembly and creates an isolated database for each gateway factory. Production persistence remains unchanged in tests. | Change disposable database provisioning in [`PostgreSqlTestDatabase.cs`](tests/CodexGateway.Testing/PostgreSqlTestDatabase.cs). Unit-test fakes remain inside the unit-test project. |

### Common changes

| Change | Follow this path |
|---|---|
| Change how a credential or project is selected | OpenAI extraction in [`OpenAiRequestMiddleware.cs`](src/CodexGateway.Api.OpenAI/Pipeline/OpenAiRequestMiddleware.cs) → protocol-neutral authorization in [`AuthenticateGatewayRequestUseCase.cs`](src/CodexGateway.Logic/UseCases/Security/AuthenticateGatewayRequestUseCase.cs) → persisted lookup rules in [`Specifications/ApiKeys`](src/CodexGateway.Logic/Specifications/ApiKeys/) and [`Specifications/Projects`](src/CodexGateway.Logic/Specifications/Projects/) → boundary tests in [`ProjectSelectorValidationTests.cs`](tests/CodexGateway.EndToEndTests/Projects/ProjectSelectorValidationTests.cs) and [`ProjectApiKeyAccessTests.cs`](tests/CodexGateway.EndToEndTests/Projects/ProjectApiKeyAccessTests.cs). |
| Change the OpenAI chat contract | Wire DTO/validator/mapper/endpoint in [`Api.OpenAI/ChatCompletions`](src/CodexGateway.Api.OpenAI/ChatCompletions/) → normalized generation models/use case only if the application capability changes → unit tests in [`tests/CodexGateway.Tests/OpenAI/ChatCompletions`](tests/CodexGateway.Tests/OpenAI/ChatCompletions/) → official-client fixtures in [`OpenAICompatibilityTests/Fixtures`](tests/CodexGateway.OpenAICompatibilityTests/Fixtures/). |
| Change project or API-key management | Persisted record in [`CodexGateway.Models`](src/CodexGateway.Models/) → named write use case in [`Logic/UseCases/Projects`](src/CodexGateway.Logic/UseCases/Projects/) or [`Logic/UseCases/ApiKeys`](src/CodexGateway.Logic/UseCases/ApiKeys/) → query specification when needed → corresponding Blazor feature folder → management and end-to-end tests. |
| Change MCP catalog/grants or add a transport | Definitions in [`Models/McpServers`](src/CodexGateway.Models/McpServers/) and project assignments in [`Models/Projects`](src/CodexGateway.Models/Projects/) → management use case and specifications → discovery in [`McpMetadataDiscoveryService.cs`](src/CodexGateway.Infrastructure.Codex/Mcp/McpMetadataDiscoveryService.cs) or runtime transport in [`Infrastructure.Mcp/Transport`](src/CodexGateway.Infrastructure.Mcp/Transport/) → MCP editor components → MCP unit/end-to-end tests. |
| Change filesystem or artifact behavior | Storage port in [`Logic/Storage`](src/CodexGateway.Logic/Storage/) only if the use case needs a new capability → focused implementation in [`Infrastructure.FileStorage`](src/CodexGateway.Infrastructure.FileStorage/) → option class when configurable → storage/security/quota tests. |
| Change container isolation or Codex execution | [`ContainerCodexRunner.cs`](src/CodexGateway.Infrastructure.Codex/Containers/ContainerCodexRunner.cs) for run orchestration, [`ContainerCommandBuilder.cs`](src/CodexGateway.Infrastructure.Codex/Containers/ContainerCommandBuilder.cs) for the security boundary, and [`ContainerRuntime.cs`](src/CodexGateway.Infrastructure.Codex/Containers/ContainerRuntime.cs) for Docker lifecycle → focused container tests → [`verify-container.ps1`](scripts/verify-container.ps1) when the production image or boundary changes. |
| Add a configuration setting | Add and document the property on the owning `*Options` class → bind/validate it in that module's installer → consume it only in the owning component → set a default in [`appsettings.json`](src/CodexGateway.App/appsettings.json) when appropriate → wire deployment overrides in Aspire/Compose → update the [configuration table](#configuration) and tests. |

Production C# follows one top-level type per file; private nested implementation details may remain with their owner. The admin UI follows `Page.razor` plus `Page.razor.cs`. MediatR is exactly pinned to the pre-license-key `12.5.0` release.

### Adding an API adapter

Create a class library named `CodexGateway.Api.<Schema>` for each additional external schema. Reference `CodexGateway.Api` and only the inward projects the adapter actually needs (`Logic` and, when its wire mapping uses persisted records, `Models`). Do not reference `CodexGateway.App`, any `CodexGateway.Infrastructure.*` project, or another API adapter.

Keep that schema's routes, wire DTOs, request validation, authentication middleware, error envelope, streaming format, and response mapping inside its adapter project. Give the module a self-contained installer that registers its concrete mappers and contributes a `GatewayEndpointAssembly` instance. Selecting the adapter means adding its project reference to `CodexGateway.App`; installer discovery handles its service registration. The low-priority FastEndpoints bootstrap and authenticated request-context accessor belong in `CodexGateway.Api`; normalized generation, model, file, and tool operations are MediatR use cases in `CodexGateway.Logic` rather than adapter services.

An adapter extracts its credential and project selector, then sends `AuthenticateGatewayRequestUseCase` to authenticate and resolve the project grant. Specifications encapsulate reusable repository reads; filesystem streaming and container execution stay explicit inside their use-case handlers rather than becoming artificial query specifications.

The OpenAI adapter retains the compatibility routes rooted at `/v1`. Give another schema its own base-path prefix, such as `/<schema>/v1/...` and `/p/{projectId}/<schema>/v1/...`. A client that only accepts a base URL can then use `https://gateway.example/<schema>` while continuing to append its native `/v1/...` paths. Each adapter owns its route recognition and project-header convention, so adding one does not extend an OpenAI-specific path switch. Hosting two adapters at the same `/v1` path on different hostnames would additionally require host-aware routing and is not part of the current bootstrap.

The adapter installer registers its endpoint assembly additively. `GatewayApiInstaller` runs last, deduplicates the selected assemblies, and disables FastEndpoints auto-discovery so an unselected adapter cannot expose routes accidentally. Register each adapter middleware explicitly in `Program.cs` (for example `app.UseMiddleware<OpenAiRequestMiddleware>()`) so route ownership, authentication, error handling, and security-sensitive ordering remain visible.

Add schema-focused unit or compatibility tests, include the project in `CodexGateway.slnx` and the Docker build context, add its project reference to the host, and add its middleware at the intended pipeline position. Architecture tests enforce dependency direction and self-contained installer ownership so adding an adapter cannot couple the inward layers or existing adapters back to the new wire schema.
