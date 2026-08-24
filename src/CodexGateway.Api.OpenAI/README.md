# CodexGateway.Api.OpenAI

This project adapts the OpenAI-compatible HTTP schema to protocol-neutral gateway use cases.

## Main flow

1. `Pipeline/OpenAiRequestMiddleware.cs` validates the API credential and project path before an endpoint runs.
2. An endpoint under `ChatCompletions/`, `Files/`, `ModelCatalog/`, or `Tools/` maps the OpenAI request to a MediatR use case.
3. The use case performs the operation without depending on HTTP types.
4. The adapter maps the result or protocol-neutral failure back to the OpenAI response shape.

## Folders

- `ChatCompletions/` — validation, mapping, endpoint, and request/response `Models/` for chat completions.
- `Files/` — upload, listing, metadata, download, and deletion endpoints with their response models.
- `ModelCatalog/` and `Tools/` — discovery endpoints.
- `Errors/` — OpenAI error models and error mapping.
- `Pipeline/` — request authentication and error-handling middleware.
- `Serialization/` — shared JSON settings.
- `Configuration/Models/` — adapter-specific settings.
- `Composition/` — this protocol module's installer.
