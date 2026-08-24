# CodexGateway.Testing

This project contains shared infrastructure required by HTTP-level test suites. It starts one disposable PostgreSQL 18 container per test process and creates a separate database for each gateway factory.

Production dependency registration is not replaced. End-to-end and OpenAI compatibility tests supply the generated connection string and exercise the same Marten repository used by a deployed gateway.
