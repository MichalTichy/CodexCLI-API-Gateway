# CodexGateway.Tests

This project contains fast unit and architecture tests grouped by the production purpose they verify.

## Main flow

1. Each purpose folder constructs the smallest relevant production object graph.
2. Fakes remain local to the test class when they are used only there.
3. `Architecture/` verifies project dependencies, installer discovery, source layout, and one-type-per-file rules.

## Folders

- `Architecture/` — structural conventions and module dependency tests.
- `Logic/` — use-case and protocol-neutral error behavior.
- `Codex/` — container execution and MCP metadata behavior.
- `Storage/` — artifact, file, and workspace safety.
- `Mcp/`, `OpenAI/`, and `Admin/` — adapter-specific unit tests.
