# PRD-023 — Visual Person Memory (chat-integrated photos)

**Status:** In progress — **023a–023c** delivered (upload, chat UX, visual tools). Next: **023d** Gemma 4 vision.

## Summary

**Visual Person Memory (VPM)** lets users attach photos in the **same chat** used for people companion workflows. Images live in **S3-compatible blob storage**; metadata and extracted facts live under scenario-scoped project memory. **Agents and `IToolActor` tools** handle ingest, Ollama **Gemma 4** vision extraction, retrieval, and privacy — not ad-hoc controller logic.

## Documents

| File | Purpose |
| --- | --- |
| [prd-023-agctor-prd.md](./prd-023-agctor-prd.md) | Goals, requirements, schemas, flow, acceptance criteria |
| [prd-023-implementation-plan.md](./prd-023-implementation-plan.md) | Phased delivery, file map, tests |
| [prd-023-ux-spec.md](./prd-023-ux-spec.md) | Composer, transcript, SSE progress, clarify chips |

## Scope tracks

| Track | Deliverable |
| --- | --- |
| **023a** | Blob store + presigned upload + asset catalog YAML |
| **023b** | Chat turn attachments + playground composer + SSE `attachment_*` events |
| **023c** | `PersonVisualIngestTool` / `PersonVisualContextTool` / `PersonVisualExtractTool` |
| **023d** | Ollama Gemma 4 `/api/chat` vision extract → generic inbox |
| **023e** | Extend `people` scenario flow (router, visual-intake, style-coach, fitness-coach) |
| **023f** | Privacy: forget-person + export purge S3; session-end visual reconcile |

## Related PRDs

| PRD | Relationship |
| --- | --- |
| **PRD-013** | Chat projects, sessions, playground SSE (`playground/message/stream`) |
| **PRD-014** | Scenario flow graph (`Router`, `LlmNode`, `toolIds`) |
| **PRD-019** | Generic inbox for visual fact proposals |
| **PRD-020** | Actor/tool patterns |
| **PRD-021** | Session-end ingest (extended for attachment manifests) |
| **PRD-022** | Privacy, forget person, export, inbox approve/reject in chat |

## Key configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `Agctor:LLM:DefaultModel` | `gemma4:31b` | Text router, personas (existing) |
| `Agctor:LLM:VisionModel` | same as `DefaultModel` | **`gemma4:31b` verified locally**; optional `e4b` for cheaper extract |
| Inference | Prompt + focus + vision | Manual tags optional; never block Send |
| `Agctor:Visual:MaxUploadBytes` | 15728640 (15 MB) | Per file |
| `Agctor:Visual:MaxAttachmentsPerTurn` | 5 | Chat composer |

## Deferred (not PRD-023)

- Calendar/contacts import
- Face recognition / biometric enrollment
- Medical diagnosis from photos
- Video / audio ingest (Gemma 4 audio is E2B/E4B only; out of scope)
- Cloud vision APIs (optional future `IVisionClient`)
