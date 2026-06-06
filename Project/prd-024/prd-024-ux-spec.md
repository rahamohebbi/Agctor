# PRD-024 — UX specification (Agctor Scenario Flow Studio)

**Principle:** Loops must be **visible, validatable, and simulatable** in the Studio — never runtime-only. Operators draw the same graph the server executes.

**Product name:** **Agctor Scenario Flow Studio** (replaces “Scenario flow designer” / “Scenario Visual Designer Modal” in all user-facing copy).

---

## 1. Entry points & chrome

| Element | Copy / behavior |
| --- | --- |
| Scenarios page button | **Open Scenario Flow Studio** |
| Modal title | **Agctor Scenario Flow Studio** |
| Modal subtitle | `{scenarioDisplayName}` · `{graphId}` · schema **2.0** badge when applicable |
| Footer | Validate · **Simulate turns** · Save flow · Cancel |

Helper text (persistent):

> Flows can pause and ask the user for more input. **At node** shows where execution waits. Domain events (e.g. photo analysis) resume the flow automatically.

---

## 2. Node palette (left rail)

### 2.1 Existing (unchanged icons, grouped “Core”)

| Type | Label |
| --- | --- |
| `ChatInput` | Chat input |
| `Router` | Router |
| `LlmNode` | Persona (LLM) |
| `Merge` | Merge |
| `Output` | Output |

### 2.2 New (“Flow control” group)

| Type | Studio label | Icon hint | Tooltip |
| --- | --- | --- | --- |
| `Gate` | **Gate** | Diamond / fork | Branch on facts (e.g. has photos) |
| `WaitForInput` | **Ask user** | Speech bubble + pause | Pause until user replies or uploads |
| `AwaitEvent` | **Wait for event** | Clock / lightning | Pause until system event (extract done) |
| `Notify` | **Notify** | Bell | Signal persona that context changed |

Drag onto canvas creates node with sensible default `label` and `config`.

---

## 3. Edge tools

### 3.1 Modes

| Mode | Studio label | Canvas style |
| --- | --- | --- |
| `sequential` | Sequential | Solid line |
| `parallel` | Parallel | Solid, “parallel” badge on fan-out |
| `loopBack` | **Loop back** | **Dashed** line, accent color (e.g. amber), arrow to **earlier** node |

### 3.2 Creating loop back edges

1. Select source node → **Add loop back** (edge tool) → click target node (must be upstream in logical flow).
2. Or: select existing sequential edge → **Convert to loop back** (if direction is backward).
3. Studio prompts for **`loopRegionId`** if not set (default: `loop-{fromNodeId}`).

### 3.3 Loop region overlay

- Toggle **Show loop regions** highlights all edges/nodes sharing a `loopRegionId`.
- Badge on region: `attempt max: 3` (from edge config).
- Invalid region (mixed `maxAttempts`) shows red outline on validate.

---

## 4. Property inspector (right rail)

Context-sensitive panels per selected node or edge.

### 4.1 Gate

| Field | Control |
| --- | --- |
| Fact | Dropdown + custom (`visual.hasPhotos`, `inbox.hasPending`, …) |
| Operator | Select: is true / is false / equals / … |
| Value | Text/number when needed |
| True branch | Edge picker (outgoing) |
| False branch | Edge picker (outgoing) |

### 4.2 Ask user (`WaitForInput`)

| Field | Control |
| --- | --- |
| Prompt | Textarea (markdown-lite) |
| Accept attachments | Checkbox (default on) |
| Attachment policy | Images only / Any |

Preview: “User will see this prompt when flow pauses here.”

### 4.3 Wait for event (`AwaitEvent`)

| Field | Control |
| --- | --- |
| Event type | Dropdown: `visual.extract.completed`, `inbox.confirmed`, … |
| Timeout (seconds) | Number, default 120 |
| On timeout | Edge picker |

### 4.4 Notify

| Field | Control |
| --- | --- |
| Target | Persona picker or actor type |
| Signal | Text |
| Include store keys | Multi-select / chips |

### 4.5 Loop back edge

| Field | Control |
| --- | --- |
| Loop region id | Text |
| Max attempts | Number (required, min 1, default 3) |
| Store invalidation | Select: from target forward / keep all / iteration only |
| Increment attempt | Checkbox (default on) |

### 4.6 Execution summary panel (bottom of inspector)

When **Simulate turns** has been run:

```
Status:     Waiting for user input
At node:    Ask user — Upload photos
Attempt:    photo-collection 1 / 3
Facts:      visual.hasPhotos = false
```

---

## 5. Validate

Runs client `validateFlowDocument` (extended) + optional server preview.

### 5.1 v2-specific errors (examples)

| Code | Message |
| --- | --- |
| `LOOP_MISSING_MAX_ATTEMPTS` | Loop back edge `{edgeId}` requires max attempts |
| `SUSPEND_NO_RESUME` | Ask user node `{nodeId}` has no resume or loop back path |
| `REGION_ATTEMPT_MISMATCH` | Loop region `{regionId}` has conflicting max attempts |
| `GATE_MISSING_BRANCH` | Gate `{nodeId}` must define true and false branches |
| `UNREACHABLE_OUTPUT` | No path from `{nodeId}` to Output |

Save is **blocked** when validation fails (same as PRD-014).

---

## 6. Simulate turns (multi-turn)

Replaces single-path **Simulate** for v2 graphs; v1 graphs keep single-path simulate.

### 6.1 Turn runner UI

```
Turn 1  [User message: "What styles suit me?"]     [Run turn ▶]
Turn 2  [User message: ""]  [📎 2 photos]        [Run turn ▶]
        ── or wait for event: visual.extract.completed [Simulate event ▶]
```

### 6.2 After each turn

- Highlight **active node** on canvas (distinct from **selected** node: blue ring = selection, green pulse = execution).
- List executed nodes in order.
- Show suspend prompt text if paused.

### 6.3 Simulate event (debug)

Dropdown of awaited event types from selected `AwaitEvent` node; injects mock payload (no real Ollama/visual in client simulate — structural only unless **Run with server** debug flag).

### 6.4 Server-backed simulate (stretch / Phase C)

`POST /api/scenarios/{id}/flow/run` with `sessionId` + `simulate: true` returns snapshot after each turn for faithful preview.

---

## 7. Canvas visual language

| State | Style |
| --- | --- |
| Selected node | Blue border |
| Execution active node | Green pulse / badge “At node” |
| Gate | Diamond shape (Cytoscape class) |
| Ask user | Rounded rectangle, pause icon |
| Wait for event | Rounded rectangle, clock icon |
| Notify | Small circle / bell |
| Loop back edge | Dashed amber, curved when possible |

---

## 8. Rename map (implementation checklist)

| Location | Old | New |
| --- | --- | --- |
| `Scenarios.cshtml` `#sc-flow-title` | Scenario flow designer | Agctor Scenario Flow Studio |
| Open button | Open visual designer | Open Scenario Flow Studio |
| `scenarios-page.js` comments | flow designer | Scenario Flow Studio |
| PRD-014 readme cross-link | Scenario Visual Designer Modal | Scenario Flow Studio (PRD-024 supersedes UX title) |

JS namespace may remain `AgctorScenarioFlow` internally; optional alias `AgctorScenarioFlowStudio` in v2.

---

## 9. Accessibility & copy

- **At node:** always paired with human `label` (not raw id alone): `At node: Ask user — Upload photos`.
- Loop edges: `aria-label="Loop back to Visual intake, max 3 attempts"`.
- Errors reference node **label** first, id in parentheses.

---

## 10. Acceptance (UX)

1. All v2 node types appear in palette and render distinctly on canvas.
2. Loop back edges are visually distinct from sequential/parallel.
3. Inspector edits round-trip to GraphDocument 2.0 on Save.
4. Simulate turns demonstrates ask → resume with **At node** display.
5. Product name **Agctor Scenario Flow Studio** visible in modal title and open button.
