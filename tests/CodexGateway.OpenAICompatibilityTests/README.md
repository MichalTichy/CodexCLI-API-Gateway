# Official OpenAI .NET compatibility tests

This project pins `OpenAI` `2.13.0` and exercises its real Chat Completions client against an in-memory Gateway host. The pin is the Gateway compatibility-suite pin, selected as the current stable NuGet release on 2026-08-17.

`D:\Work\Samwise-Assistant` contained scaffolded .NET projects when this suite was created, but none referenced `OpenAI`, `Microsoft.Extensions.AI.OpenAI`, or Microsoft Agent Framework, and it had no central package props or package lock supplying those integration versions. Therefore this version must not be described as Samwise's package pin. Once Samwise checks in its actual integration package versions, align this project to them and review the sanitized fixture diff as a wire-contract change.

Microsoft Agent Framework and `Microsoft.Extensions.AI.OpenAI` are intentionally not referenced until Samwise provides its exact compatible pins. Raw Gateway tests cover their unsupported request shapes in the meantime; this suite establishes the official OpenAI client boundary that those adapters ultimately use.

The suite and sanitized fixtures cover:

- Project-scoped base URL composition and bearer authentication.
- Plain non-streaming Chat Completions.
- Named system, developer, user, and assistant history, including text-part arrays and Unicode.
- SSE reconstruction, terminal stop, and the `stream_options.include_usage: true` shape emitted automatically by this SDK version.
- Strict `json_schema` structured output.
- Explicit rejection of a real SDK-generated local function-tool request.

Agent Framework session serialization remains deferred until its actual Samwise packages are selected; the history replay test does not claim to be an Agent Framework session test.

Wire expectations follow the [official Chat Completions API reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create). The SDK calls themselves use the public API documented in the README shipped with the pinned official package.
