# CodexGateway.Infrastructure.Storage

This project implements filesystem storage for uploads, project artifacts, and isolated run workspaces.

## Main flow

1. `Files/FileStore.cs` validates an upload and writes its content and metadata below the configured storage root.
2. `Workspaces/WorkspaceManager.cs` creates a run workspace and stages project or temporary files into it.
3. Codex runs against the workspace; artifact guards enforce per-file and total limits while the run is active.
4. A successful project run commits its artifact tree; temporary uploads and abandoned workspaces are cleaned up independently.

## Folders

- `Files/` — upload persistence and projectless-file cleanup.
- `Projects/` — project artifact-directory lifecycle.
- `Workspaces/` — staging, committing, and deleting run workspaces.
- `Artifacts/` — safe file access and quota enforcement; data-only values are under `Artifacts/Models/`.
- `Configuration/` — resolved filesystem paths.
- `Composition/` — this module's installer.
