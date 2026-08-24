# CodexGateway.McpGateway.Stdio

This project implements process-based STDIO forwarding for gateway-hosted MCP servers.

`StdioMcpUpstreamFactory` resolves the explicitly configured environment variables and starts
one `LocalStdioGatewayMcpUpstream` process for the run-scoped session. Requests are written to
stdin, responses are read from stdout, and the process is terminated when the session ends.
