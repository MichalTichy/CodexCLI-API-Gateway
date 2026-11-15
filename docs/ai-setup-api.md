# AI-assisted setup API

The optional setup API lets an AI assistant configure an existing Gateway without
automating the admin UI. It uses the same application operations and validation as
the UI. It does not perform model requests or grant tools implicitly.

Enable it temporarily by setting `Setup__ApiKey` to a separately generated random
secret (32 random bytes or more). Pass that value in the `X-Setup-Key` request header.
Use HTTPS or a trusted local network. Keep the secret in an ignored local secret
file, never in prompts, source control, command arguments, or captured output.
Remove `Setup__ApiKey` and restart Gateway when setup is complete. When no key is
configured, these endpoints return 404. Gateway API keys and admin cookies do not
authorize setup calls. Every setup response has `Cache-Control: no-store`.

All paths below are relative to `/admin/setup`. JSON uses camelCase and numeric
enum values: `executionMode: 1` means Gateway-managed MCP and `webSearchMode: 0`
disables web search. No setup call changes existing keys or rotates their secrets.

| Method and path | Body / result |
| --- | --- |
| `GET /state` | `{apiKeys:[{id,name}],projects:[...],mcpServers:[...]}`. API key secrets are excluded by the database projection. Projects include exact current `apiKeyAccess`; server definitions contain environment-variable names, never resolved environment values. |
| `POST /api-keys` | `{id,name}` → `{id,created,key}`. `key` is returned only on creation; an identical retry returns `created:false,key:null`. A different name under an existing ID returns 409. |
| `PUT /projects/{id}` | `{name}` → project. Creates if absent; preserves an existing matching project and all its settings. A different name returns 409. |
| `POST /mcp-servers/discover` | Full server definition → discovered server info, tool descriptions, input schemas, and annotations. Performs metadata discovery without storing or granting anything. |
| `PUT /mcp-servers/{id}` | Full server definition → registered server. The body ID must match the route. Identical registration is repeatable; different existing settings return 409. Review current configuration and use `?replace=true` only when explicitly intending replacement. |
| `PUT /projects/{projectId}/access/{apiKeyId}` | `{apiKeyId,webSearchMode:0,mcpServers:[{serverId,required:false,enabledTools:[...]}]}` → updated project. Replaces only this key's access; preserves other keys, runner image, and project settings. Validates key, enabled servers, and exact available tool names. An empty array grants project access without MCP tools. |
| `GET /codex` | Current account and device-login state. |
| `POST /codex/device-login` | Starts device login. The user completes verification at the displayed provider URL; inspect `/codex` until authenticated. Never copy another identity's auth volume. |

HTTP MCP registration example:

```json
{
  "transport": "http",
  "id": "samwise",
  "name": "Samwise",
  "enabled": true,
  "executionMode": 1,
  "url": "http://web:8080/mcp",
  "environmentHeaders": { "X-Api-Key": "SAMWISE_MCP_API_KEY" },
  "environmentVariables": [],
  "availableTools": ["get_agent_settings", "list_playbooks"]
}
```

An STDIO definition uses `transport:"stdio"`, `command`, `arguments`, and
`environmentVariables` in place of `url` and `environmentHeaders`. Its executable
must be installed in the Gateway container when `executionMode` is 1. Environment
names are validated against the existing credential-forwarding policy.

Discover metadata first, review tool behavior with the user, then select an exact
`availableTools` list and per-key grants. Tool names newly discovered later remain
ungranted until deliberately added. Tool annotations help review but are not proof
of safety. Keep planning/intake non-mutating and execution separately credentialed.
Samwise records the automatic-approval catalog for audit and requests only a
decision and reason. Tools exposed by that profile do not themselves block
assessment; limit them to that review role. Its execution profile must use a
separate restricted grant set.

On resume, read `/state` and the assistant's saved local credential file. Reuse
credentials and existing access; do not submit empty access over configured grants.
If an API-key creation response is lost before saving its secret, a repeat call
will not reveal it. Use the admin UI to revoke that unused key and create a new
explicitly named key, then update the consumer and grants deliberately.

Errors use `{error,message}` with HTTP 400 for invalid input, 401 for a missing or
incorrect setup key, 404 when disabled or a selected resource is missing, and 409
for existing-resource collisions. Unexpected upstream errors return a sanitized
500; do not blindly repeat mutations after an uncertain response. Read state first.
