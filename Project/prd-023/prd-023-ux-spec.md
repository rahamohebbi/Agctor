# PRD-023 — UX specification (Visual Person Memory)

**Principle:** Photos feel like sending a message in iMessage/WhatsApp — attach, type (or not), send. Intelligence happens **after** send, in the thread, without blocking the conversation.

## 1. Composer (playground input area)

### 1.1 Attach affordances

| Control | Behavior |
| --- | --- |
| **Paperclip** | Opens file picker (`image/*`) |
| **Drag & drop** | Drop zone = entire composer; highlight on dragover |
| **Paste** | `Ctrl+V` image from clipboard creates pending attachment |

### 1.2 Attachment chips (before send)

Each selected file becomes a **chip** above the text input:

```
┌──────────────┐
│ [thumb]  ✕   │  Uploading… 45%
│  gym.jpg     │
└──────────────┘
```

| State | Chip UI |
| --- | --- |
| Uploading | Thumbnail + thin progress bar; Send **disabled** only until all chips reach `uploaded` |
| Ready | Thumbnail + subtle check; optional **“Tag”** link (not required) |
| Failed | Red border + “Retry” |

**Send rule:** Enabled when text non-empty **OR** at least one chip is `uploaded`. Never require tags.

### 1.3 Optional tag panel (collapsed by default)

Expanding **Tag** on a chip opens an **inline popover** (not a modal):

- Entity search combobox (people in active scenario)
- Role: Primary / Also in photo
- Caption (single line)
- Privacy: Normal / Sensitive / Don’t analyze

Pre-select **project focus** entity as Primary when set.

### 1.4 Composer placeholder copy

- No attachments: `Message…`
- With attachments: `Add a note (optional) — e.g. “leg day week 2”`

## 2. Send flow (happy path)

```
Attach → auto-upload (presigned) → user types (optional) → Send
         ↓
    Stream starts immediately (SSE llm_delta)
         ↓
    Bubble footer: "Analyzing photo…" (spinner)
         ↓
    Bubble footer: "Insights ready · 3 memories to review" (link → inbox)
```

User never leaves the chat column.

## 3. User message bubble (transcript)

```
┌─────────────────────────────────────────┐
│  [ inline image grid — 1–3 thumbs ]      │
│  How's my gym progress?                  │
│  ─────────────────────────────────────   │
│  ○ Analyzing photo…          (async)     │
└─────────────────────────────────────────┘
```

| Element | Detail |
| --- | --- |
| Images | Click → lightbox with signed URL |
| Footer states | `uploading` → `analyzing` → `ready` / `needs_review` |
| Actions (⋯ menu) | Tag people, Don’t analyze, Delete photo |

After inference without manual tags, show subtle line: `Understood: Raha · fitness` (editable via Tag).

## 4. Assistant message

- References photo naturally (“In this gym mirror shot…”).
- If extract still running: “I’ll note details for your memory once analysis finishes.”
- Link **Review suggested memories** when inbox count &gt; 0 (anchors to existing Confirmation inbox panel).

## 5. Clarify (low confidence only)

**Not a modal.** Inline **chip row** under the user bubble:

```
Who should we save this for?  [ Raha ] [ Ryan ] [ Someone else… ]
```

- Appears when `inference.confidence < 0.45` **and** user asked to save/remember
- For style/fitness questions, **never block** — answer first, chips optional

## 6. SSE-driven UI (no polling)

| SSE `type` | UI update |
| --- | --- |
| `attachment_state` | Chip / bubble footer progress |
| `attachment_preview` | Swap thumb blob → signed URL |
| `visual_extract_started` | Footer “Analyzing…” |
| `visual_extract_done` | Footer “Insights ready”; inbox badge +1 |
| `visual_inbox` | Reminders strip badge count |
| `flow_step` | Existing flow chips include `pm-visual-*` |

## 7. Reminders / inbox integration

- Existing **Confirmation inbox** panel shows visual proposals with **thumbnail**.
- Row copy: `From photo · gym.jpg · Raha · footwear: white trainers`
- Approve / Reject unchanged (PRD-022).

## 8. Project rail context

- Active **focus person** avatar/name in project header (existing) doubles as default infer target.
- Tooltip: “Photos you send are understood in context of {name} unless you say otherwise.”

## 9. Empty / error states

| Case | UX |
| --- | --- |
| Ollama vision down | Toast: “Photo saved. Analysis unavailable — is Ollama running with gemma4:31b?” |
| File too large | Chip error before upload starts |
| Unsupported type | Inline “Use JPG, PNG, or WebP” |

## 10. Visual design notes

- Reuse playground typography and `flow_step` colors for consistency.
- Attachment chips: rounded-lg, subtle shadow, match dark/light dashboard theme.
- Progress: indeterminate bar on chip; determinate % on slow networks.
- Avoid second column or wizard steps for photos.

## 11. Accessibility

| Requirement | Implementation |
| --- | --- |
| Keyboard | Paperclip focusable; Enter on chip ✕ removes |
| Screen reader | `aria-busy` on uploading; announce “Photo analysis complete” on SSE done |
| Alt text | Populate from extract `sceneTags` when available |

## 12. UX acceptance checklist

- [x] Attach + Send in 2 actions with no required tag step
- [x] Assistant text begins &lt; 2s after Send (extract async; stream never blocked)
- [x] User sees upload and analysis progress without opening Trace
- [x] Optional Tag popover works on sent and unsent chips
- [x] Inbox thumbnail visible on pending visual rows (`SourceAssetId` on proposals)
