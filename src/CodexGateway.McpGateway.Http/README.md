# CodexGateway.McpGateway.Http

This project implements authenticated HTTP forwarding for gateway-hosted MCP servers.

`HttpMcpUpstreamFactory` reads configured header values from the gateway environment and
creates `HttpGatewayMcpUpstream` instances. The upstream forwards MCP requests and responses
without exposing the configured credentials to the runner container.

The transport itself remains protocol-neutral. Run-local materialization of explicitly marked
`artifact://` binary resources is performed by `CodexGateway.McpGateway` after forwarding.
