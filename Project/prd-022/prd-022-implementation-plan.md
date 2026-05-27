# PRD-022 — Implementation plan

## Phase 022a — Confirmation inbox

1. `IGenericInboxDecisionService` + `GenericInboxDecisionService` (list, approve+replay, reject).
2. Optional `GenericInboxReviewActor` + facade (PRD-020 style); Host calls service directly for v1.
3. DTOs + `ProjectMemoryController` endpoints.
4. Playground: `#pm-play-inbox` panel, `loadInbox()`, wire after project change and Send.

## Phase 022b — Privacy

1. `CompanionPrivacySettings` + `CompanionPrivacySettingsStore` → `.agctor/runtime/companion-privacy.yaml`.
2. `IPrivacyMemoryService` — get/set settings, forget person, export zip.
3. Gate `TrySessionEndIngestAsync` on `AutoIngestOnSessionEnd`.
4. Playground Privacy `<details>` with toggle, forget, export.

## Tests

| Test | Project |
| --- | --- |
| `GenericInboxDecisionServiceTests` | Core.Tests |
| `PrivacyMemoryServiceTests` | Core.Tests |
| Optional HTTP smoke | Host.IntegrationTests |

## Deferred

- Calendar/contacts import → future PRD (after PRD-023 Visual Person Memory)
