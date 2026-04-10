# PRD: UX & UI — Project Memory, Agent Studio & Storage Rules (Dashboard)

**Document type:** UX / product design specification (no implementation in this document)  
**Folder:** `Project/prd-013/`  
**Parent specification:** [prd-013-agctor-prd.md](./prd-013-agctor-prd.md) (especially §18 UX requirements)  
**Relationship:** This PRD describes the **user-facing experience** and **UI scope** for features that the core PRD defers to post-MVP (Agent Studio, Schema Studio, workspace browser, guided project setup). It assumes the **file-canonical model** (`.agctor/`, YAML agents, schemas) remains the runtime truth; the Dashboard is the primary place to author and validate that truth without hand-editing every file.

**Version:** 1.0 (draft)  
**Status:** Specification only — implementation planning follows separately.

---

## 1. Purpose

Deliver an **intuitive Dashboard experience** so operators can:

1. **Create and edit portable agents** (YAML-aligned) with clear sections for role, tools, memory access, contracts, and guardrails — not by memorizing file formats.
2. **Define and maintain storage rules** — entity types, document types, routing rules, workspace layout, and update modes — in forms and visual flows that mirror the domain model in [prd-013-agctor-prd.md](./prd-013-agctor-prd.md).
3. **Use templates** to start from vetted patterns (e.g. “People extractor”, “Memory curator”, “Query agent”) and customize safely.
4. **Stay aligned with rebuild and validation** — see errors/warnings before and after writes, and trigger rebuild/import from the UI.

This PRD does **not** prescribe specific frameworks, component libraries, or API contracts; those belong in a technical plan.

---

## 2. Goals

### 2.1 Primary goals

1. **Reduce cognitive load**: New users can create a valid agent + minimal schema path without reading the full parent PRD.
2. **Single place for project memory**: Dashboard becomes the home for “what agents exist” and “where data may live” for the active project.
3. **Template-led creation**: Wizards or “New from template…” flows produce consistent, reviewable YAML (or equivalent persisted artifacts) with human-readable previews.
4. **Safe by default**: Tool allow/deny and `memoryAccess` globs are explained inline; dangerous combinations are blocked or require explicit confirmation.
5. **Discoverability**: Users find Schema rules, Agents, Templates, and Rebuild from consistent navigation.

### 2.2 Secondary goals

1. **Parity with power users**: Advanced mode still allows direct YAML/markdown editing where needed (with validation).
2. **Consistency with existing Dashboard**: Reuse patterns from PRD-012-style pages (layout, config persistence, feedback) where applicable.
3. **Accessibility**: Forms usable with keyboard, labels, and readable error association (baseline WCAG-oriented intent; detailed audit in implementation).

---

## 3. Non-goals (this PRD)

1. Replacing canonical files with a database as source of truth.
2. Implementing semantic search / vector UI (may surface later as optional panels).
3. Multi-tenant SaaS admin UX (single-project or single-workspace focus is enough for v1 of this UX).
4. Mobile-native apps (responsive web is sufficient unless a separate mobile PRD exists).
5. **Code**, APIs, database migrations, or file paths in `AgctorSDK.*` — out of scope here.

---

## 4. Target users & scenarios

| Persona | Needs |
| --- | --- |
| **Author** | Define a new agent (e.g. extractor) from a template, tune instructions and tool scope, save to project. |
| **Curator / admin** | Adjust routing rules and document types when the team adds a new “knowledge type”. |
| **Reviewer** | Compare agent specs and schema diffs before merge; run validation. |
| **Operator** | Import folder, rebuild index, read logs/errors after a bulk edit. |

**Core scenario (happy path):** User selects project → opens **Agent Studio** → “New agent from template” → picks “Person extractor” → edits name, instructions, tightens `memoryAccess` → saves → UI validates → files updated (or PR generated) → user runs **Rebuild** from **Project health** and sees success.

**Secondary scenario:** User opens **Storage rules** → edits routing table (“when `knowledgeType` X → document Y, section Z”) → validation catches orphan document types → user fixes in same session.

---

## 5. Design principles

1. **Files are truth, UI is clarity** — Every save should map to the portable layout under `.agctor/`; show a **preview** or **diff summary** before commit when possible.
2. **Progressive disclosure** — Templates and “Simple” mode hide advanced fields (e.g. `runtimeHints`); “Advanced” expands full YAML-shaped options.
3. **Explain security** — Tooling and path globs are security boundaries; short help text + examples (`people/**`, `schemas/**`).
4. **Validation-first** — Inline field validation + project-level validate action; block save on hard errors, warn on soft issues.
5. **Cohesive navigation** — One **Project memory** (or similarly named) area grouping Agents, Storage rules, Templates, Files, Rebuild.

---

## 6. Information architecture (Dashboard)

Suggested top-level structure (names are indicative; final copy in implementation):

```
Dashboard
└── Project memory          [new section]
    ├── Overview            [project card: type, runtime mode, path, health]
    ├── Agents              [Agent Studio list + create]
    ├── Storage rules       [Schema Studio: entity & doc types, routing, workspace]
    ├── Templates           [browse + “apply template” / duplicate into project]
    ├── Workspace           [browser: entities, views, .agctor tree]
    └── Import & rebuild    [import path, validate, rebuild, logs]
```

**Entry points:**

- From global nav: **Project memory** (visible when a project context is selected or after “Open project”).
- Optional **quick action** on home: “New agent from template” if project is loaded.

---

## 7. Feature specifications

### 7.1 Project overview (Project memory → Overview)

**Purpose:** Orient the user and show health at a glance.

**Content (minimum):**

- Active project name / id, project type (e.g. people), runtime mode (SQLite / Postgres).
- Resolved project root path (read-only or editable with warning).
- **Status chips:** Valid / Warnings / Errors (from last validation or rebuild).
- Short **last rebuild** timestamp and link to logs.
- Primary actions: **Validate project**, **Rebuild index**, **Open folder** (optional, desktop).

**UX notes:**

- Empty state when no project: prompt to create or import — short explanation of portable `.agctor` projects.

---

### 7.2 Agent Studio (list + editor)

**Purpose:** CRUD for `*.agent.yaml`-equivalent definitions per [parent PRD §8](./prd-013-agctor-prd.md).

**List view:**

- Table or cards: name, id, role, compatible project types, last modified.
- Filters: by project type, by role (extraction, curation, query, …).
- Actions: **New agent**, **New from template**, duplicate, delete (with confirm).

**Editor (sections — align with §18.2):**

| Section | User intent | Notes |
| --- | --- | --- |
| Overview | Identity | id, name, role, description, project type compatibility (multi-select). |
| Instructions | Prompting | Bulleted instructions; optional “system vs user” split later. |
| Tools | Capability | Allow list / deny list; tool ids searchable; link to tool help. |
| Memory / file access | Safety | Read globs, write globs or special tokens (`memory_intents_only`, `schema_allowed_targets_only`); live examples. |
| Contracts | Integration | Input type, output type (dropdowns matching known enums / free text with validation). |
| Guardrails | Policy | Bullet list strings. |
| Runtime hints | Ops | Preferred model, preferred mode (sqlite/postgres/any) — optional collapsed. |

**Behaviors:**

- **Validate** on save; show field-level and cross-field errors (e.g. deny overrides allow).
- **Preview** tab: read-only YAML (or structured JSON) reflecting what will be written.
- **Conflict detection** if id collides with existing agent.

---

### 7.2.1 Scenario editor — dual roster UX (runtime vs persona)

**Purpose:** Remove ambiguity between actor runtime topology and non-runtime project-memory personas.

For each scenario in `/Dashboard/Scenarios`, provide two independent controls:

1. **Runtime agent roster** (`agentTypes`)
   - chip picker for registered C# types
   - add/remove/clear, autocomplete from known runtime agent types
   - validation: unknown, disabled, duplicate
2. **Persona roster (YAML, non-runtime)** (`personaAgentIds`)
   - chip picker for definitions where `kind = project-memory-yaml`
   - add/remove/clear, autocomplete from unified definitions catalog
   - validation: unknown persona id, duplicate, optional projectType mismatch warning

Optional role mapping:

- `personaBindings`:
  - extractor
  - curator
  - query

**Preview language in UI:**

- “Will bootstrap runtime agents: …”
- “Will attach persona profile for project-memory flows: …”

**Design rule:** Personas are clearly labeled as non-runtime configuration and must not be presented as spawned actors.

---

### 7.3 Template gallery & guided creation

**Purpose:** Guide creation so users do not start from a blank form.

**Template types:**

1. **Agent templates** — Pre-filled Agent Studio fields matching known patterns (e.g. person-extractor, memory-curator, person-query). Metadata: description, recommended project types, required tools.
2. **Storage rule starter packs** (optional v1.1) — Bundled entity + document + routing *skeletons* for a project type variant.

**Flows:**

- **New agent from template:** Wizard steps: (1) pick template, (2) set id + display name, (3) choose project types, (4) customize instructions + memory access (with smart defaults), (5) review → save.
- **Template detail page:** What this template is for, which files it will create or merge, limitations.

**UX notes:**

- Templates are **not** a second source of truth; they **seed** files under `.agctor/agents/` (and optionally link to doc templates).
- Show **diff** if saving would overwrite an existing agent.

---

### 7.4 Storage rules (Schema Studio)

**Purpose:** Authoring UX for rules that map to `schemas/<type>/` per parent PRD §9.

**Sub-areas (can be tabs or left nav):**

1. **Project type** — Display name, version, references to other schema files.
2. **Entity types** — Folder patterns, metadata file name, required/optional documents.
3. **Document types** — File name, purpose, **update mode** (replace_section, merge_list, append_chronological), section list.
4. **Routing rules** — Table or rule builder: condition (`knowledgeType`, optional `attribute`) → target document + section. Support reorder (first match wins).
5. **Workspace schema** — Roots, entity views, index views.

**UX patterns:**

- **Routing matrix** alternative: rows = knowledge types, columns = documents/sections — only if usability testing supports it; default to rule list + add row.
- **Validation:** Orphan routes, missing document types, invalid section names vs document template.

---

### 7.5 Workspace browser

**Purpose:** Read-centric navigation of canonical tree (parent §18.4).

**Minimum:**

- Tree or split view: `people/`, `views/`, `.agctor/agents/`, `.agctor/schemas/`.
- Open file in **preview** (markdown rendered); optional “Edit externally” link.
- Deep link from Agent Studio (“open related entity folder”) when applicable.

**Non-goal for v1:** Full in-browser markdown editor parity with VS Code (optional phase).

---

### 7.6 Import & rebuild

**Purpose:** Operational confidence (parent §18.5).

**Flow:**

1. User specifies path or uploads a folder (implementation decision later).
2. **Validate** runs; results grouped: errors / warnings / info with clickable paths.
3. **Rebuild** triggers index rebuild; progress indicator; link to log file summary.
4. **Success/failure** state with retry and “copy log” for support.

**UX:** Destructive actions (overwrite, delete entities) require confirmation and show scope.

---

## 8. Visual & interaction design (guidelines)

1. **Layout:** Use existing Dashboard shell (header, nav, content width) for consistency.
2. **Forms:** Group related fields; use sidebars for help on globs and tool ids.
3. **Feedback:** Toast or inline banner for save/rebuild; persistent **notification center** optional for long rebuilds.
4. **Empty states:** Every list (agents, rules, templates) has a short explanation + CTA.
5. **Terminology:** Glossary tooltips — *memory intent*, *routing*, *canonical entity*, *rebuild* — linked to one help doc.

---

## 9. Success metrics (product)

| Metric | Target direction |
| --- | --- |
| Time to create first valid agent (from template) | Down |
| Validation errors caught before file hand-off | Up |
| Support questions about globs / tools | Down over time |
| Successful rebuild after UI-driven edits | Up |

(Exact baselines TBD after first release.)

---

## 10. Phased delivery (UX scope)

| Phase | Scope |
| --- | --- |
| **UX-A** | Nav + Project overview + Agent Studio (list + editor) + save/validate + YAML preview; **agent templates** (minimum 3). |
| **UX-B** | Storage rules (Schema Studio) for People project type end-to-end + routing UI. |
| **UX-C** | Workspace browser + Import/rebuild UI wired to backend. |
| **UX-D** | Polish: advanced YAML edit, diff view, optional storage “starter packs”, analytics. |

Phases are **UX slices**; engineering may sequence differently.

---

## 11. Acceptance criteria (UX-level)

1. User can create a new agent from a named template without editing raw YAML in the default path.
2. User can edit all Agent Studio sections listed in §7.2 with validation feedback.
3. User can define at least one routing rule via UI and see it reflected in a preview/export consistent with parent PRD schema shapes.
4. User can run **Validate** and **Rebuild** from the UI and see pass/fail with actionable messages.
5. Navigation matches §6 structure or an documented equivalent map.

---

## 12. Open questions

1. **Multi-project:** Single active project vs project switcher — product decision.
2. **Git integration:** Show diff / commit from UI? Optional phase.
3. **Permissions:** Read-only roles for reviewers — future RBAC PRD?
4. **Localization:** English-only v1 acceptable?

---

## 13. References

- [prd-013-agctor-prd.md](./prd-013-agctor-prd.md) — §7 structure, §8 agents, §9 schemas, §12 memory pipeline, §18 UX, §24–25 MVP vs Phase 2.
- [prd-013-implementation-plan.md](./prd-013-implementation-plan.md) — current backend module locations (for future alignment only).

---

## Document history

| Version | Date | Notes |
| --- | --- | --- |
| 1.0 | TBD | Initial UX/UI PRD for Dashboard Agent Studio & storage rules. |
