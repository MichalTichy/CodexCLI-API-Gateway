# CodexGateway.McpGateway.Http

This project implements authenticated HTTP forwarding for gateway-hosted MCP servers.

`HttpMcpUpstreamFactory` reads configured header values from the gateway environment and
creates `HttpGatewayMcpUpstream` instances. The upstream forwards MCP requests and responses
without exposing the configured credentials to the runner container.
