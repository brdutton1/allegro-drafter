# Allegro Drafter: rules for AI working on this repo

AI drafting assistant for Revit 2025/2026. The designer stays in control: the AI proposes, a human approves.

## Non-negotiable
- **No model change without approval.** Every write goes through a `ProposalChange` and `ProposalApplier`. Never add a tool that edits the model directly.
- **No invented names.** Anything the AI picks (title blocks, templates, types, tags) must be validated against the standards profile in `ProposalValidator`, and checked against the live model in `ModelChecker`.
- **One approval = one transaction.** All or nothing. Roll back on any failure.
- **Core stays Revit-free.** `src/AllegroDrafter.Core` must never reference the Revit API. Put logic there when possible so it can be tested.
- **All Revit API calls run through `RevitDispatcher`.** Never call the API from the HTTP thread or a WPF click handler.
- **No XAML.** The pane is built in C# so the add-in compiles on Linux CI.

## Build and test
```
dotnet test tests/AllegroDrafter.Core.Tests
dotnet build src/AllegroDrafter.Revit -p:RevitVersion=2025
dotnet build src/AllegroDrafter.Revit -p:RevitVersion=2026
```
Revit itself only runs on Windows. Compiling proves the API calls exist, not that they behave. Say so when reporting.

## Writing style for user-facing text
Short, plain, blunt. Tell the designer what happened and what to do next. No em dashes.
