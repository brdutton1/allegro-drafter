# Architecture

## Pieces

| Project | Runs where | Job |
|---|---|---|
| `AllegroDrafter.Core` | Revit + bridge + tests | Proposal model and store, validation against standards, standards profile, audit log, MCP protocol, localhost HTTP server, bridge relay. No Revit API references, so it builds and tests anywhere. |
| `AllegroDrafter.Revit` | Inside Revit 2025/2026 | Ribbon, approval pane, ExternalEvent dispatcher, model reads, model checks, transactional apply, standards export. |
| `AllegroDrafter.Bridge` | Started by Claude | stdio MCP server. Serves `initialize` and `tools/list` itself, forwards `tools/call` to Revit. |

## A proposal's life

1. Claude calls `propose_sheet_set` (or another `propose_*` tool).
2. `ProposalRequestParser` turns the arguments into typed changes. Bad input fails with the exact field path.
3. `ProposalValidator` checks names against `standards.json`, duplicate numbers, and the 50-change cap.
4. `ModelChecker` runs on the Revit thread: sheet numbers free, views exist and aren't already placed, templates fit view types, title block loaded.
5. `ProposalStore.Add` queues it as **Pending**. The approval pane pops open.
6. Designer clicks **Approve**. `TryBeginApply` flips it to **Applying** under a lock, so a double click can't apply twice.
7. `ProposalApplier` re-runs `ModelChecker` (the model may have changed), then writes everything in **one** named transaction. Any exception rolls back the whole batch.
8. Result is **Applied** or **Failed** (retryable). Audit log gets a line either way. Reject writes a line too.

## Threading

The Revit API only works on Revit's main thread inside an API context. The HTTP server and the pane's button clicks are not in that context. Everything that touches the model goes through `RevitDispatcher`: queue the work, raise an `ExternalEvent`, Revit runs it when idle.

If Revit doesn't get to it within the timeout (30 s for reads, 5 min for applies), usually because a modal dialog is open, the work is cancelled and never runs. A work item that already started is always allowed to finish.

## Security

- HTTP listens on `localhost` only.
- Every request needs the bearer token from `%APPDATA%\AllegroDrafter\mcp.json` (random 256-bit, compared in constant time).
- Requests with a non-localhost `Origin` header are refused, so a web page can't drive Revit.
- Worst case if someone gets the token anyway: they can read the model and queue proposals. They still can't change anything without a human clicking Approve.
- No Anthropic API key lives in the add-in. Claude Desktop / Claude Code holds it.

## Why MCP instead of a chat box inside Revit

- Swappable client: Claude Desktop today, Claude Code for power use, Studio OS later. No rewrite.
- Conversation, file uploads (old plan set PDFs) and model choice come from the Claude app for free.
- The add-in stays small: read, check, propose, apply.

## Adding a new kind of change

1. Add a `record` deriving from `ProposalChange` in `Core/Proposals/ProposalChange.cs` and register it with `[JsonDerivedType]`.
2. Parse it in `ProposalRequestParser`, validate names in `ProposalValidator`. Add tests.
3. Add the tool to `ToolNames` and `ToolCatalog`.
4. Check it in `Revit/Model/ModelChecker.cs` and apply it in `Revit/Model/ProposalApplier.cs`.
