# AGCTOR PRD: Generic Agents + File-Canonical Project Memory

## 1. Overview

AGCTOR needs a generic agent framework that allows users to create specialized agents through configuration instead of hardcoding each agent type. The system must support multiple project types, each with its own memory layout, entity types, document structures, and routing rules.

The core design principle is:

**Portable files are the canonical truth.**  
The runtime database is a rebuildable acceleration layer.

A project must be restorable from its project files alone, without requiring a Postgres backup. Markdown is the main human-readable storage format, supported by lightweight YAML manifests and metadata files where needed. The runtime may use SQLite or Postgres depending on deployment mode, but both must operate on the same canonical file structure.

The system must support:
- generic agent creation from configuration
- configurable tool access
- project-specific schemas
- project-specific folder and document layout
- portable markdown-first memory
- multi-agent collaboration
- runtime rebuild from files
- multiple runtime modes:
  - SQLite for local/lightweight mode
  - Postgres for multi-user/high-performance mode

This PRD covers the final design for:
- generic agents
- project schemas
- markdown-first memory
- portable project structure
- runtime rebuild architecture
- examples for People, Sales Lead, and Job Search projects
- UX requirements
- implementation boundaries for MVP and near-term phases

## 2. Goals

### 2.1 Primary goals

Build a system where:
1. A user can define and run generic agents using configuration files.
2. A project defines how memory and workflow artifacts are organized.
3. Human-readable markdown files are the durable canonical representation.
4. The system can rebuild its runtime state from project files alone.
5. The same project package works in SQLite mode and Postgres mode.
6. Multiple agents can collaborate through shared canonical project files.
7. Different project types can define different folder and file layouts.
8. Agents can read/write only what the project schema allows.
9. The system supports operational workflows, not just static memory.

### 2.2 Secondary goals

1. Make the system easy to inspect, version, export, and restore.
2. Keep the architecture simple enough for an MVP.
3. Avoid over-coupling to any one actor runtime or database backend.
4. Make it easy to add new project types and agent roles later.

## 3. Non-goals

For the initial implementation, AGCTOR will not:
1. Depend on database backups for portability.
2. Require a separate vector database.
3. Require graph databases.
4. Implement full autonomous memory mutation without schema control.
5. Rely on prompt-only storage behavior.
6. Require enterprise-grade multi-tenant access control in MVP.
7. Implement all possible project types up front.

## 4. Core principles

### 4.1 File-canonical architecture
Portable files are the canonical truth. These include:
- markdown documents
- YAML manifests
- YAML schema definitions
- YAML agent definitions
- minimal entity metadata files

### 4.2 Database as rebuildable runtime layer
SQLite or Postgres may store:
- indexes
- parsed document sections
- search acceleration
- embeddings later
- execution state
- caches

But none of these are required to restore the project.

### 4.3 Project-driven memory
Agents do not define memory layout by prompt alone.  
Projects define:
- entity types
- file layout
- document types
- routing rules
- update modes
- views

Agents operate within project rules.

### 4.4 Generic agent, explicit contracts
An agent is not just a prompt. It has:
- identity
- role
- instructions
- tools
- access permissions
- input/output contracts
- project compatibility
- guardrails

### 4.5 Markdown-first, metadata-light
Markdown is the main human-readable storage layer.  
YAML sidecars are allowed where needed to preserve:
- entity identity
- aliases
- schema version
- durable metadata

### 4.6 Same project format in all runtime modes
SQLite mode and Postgres mode must use the same canonical project package.

## 5. Core product concepts

### 5.1 Project
A project is the top-level portable package. It contains:
- project manifest
- schema definitions
- agent definitions
- templates
- canonical entity/workflow files
- optional views

Examples:
- People Project
- Sales Lead Project
- Job Search Project
- Software Project

### 5.2 Project type
A project type defines:
- supported entity types
- document types
- routing rules
- folder grammar
- workspace rules

Examples:
- people
- sales
- jobs
- software

### 5.3 Entity
An entity is a canonical domain object.

Examples:
- person
- business
- company
- application
- service
- feature

Each entity usually has:
- a folder
- an `entity.yaml`
- one or more markdown docs

### 5.4 View
A view is a human-readable summary/index over canonical entities.

Examples:
- `views/people-index.md`
- `views/pipeline/hot-leads.md`
- `views/searches/2026-03-15-backend-dotnet-oc.md`

Views are not the only truth. They summarize or organize canonical entities.

### 5.5 Agent
An agent is a configured role that can perform work using approved tools and schema-defined access.

Examples:
- person-extractor
- memory-curator
- job-discovery-agent
- resume-tailor-agent
- sales-outreach-agent

### 5.6 Schema
A schema defines project structure and persistence behavior.

Examples:
- project type definition
- entity type definitions
- document type definitions
- routing rules
- workspace schema

## 6. Runtime modes

### 6.1 SQLite mode
Use for:
- single machine
- solo user
- local development
- smaller projects
- low ops complexity

Characteristics:
- canonical files on local filesystem
- SQLite for runtime indexing and caching
- simple local rebuild
- no server dependency required

### 6.2 Postgres mode
Use for:
- multi-user systems
- multiple agents running concurrently
- shared project hosting
- larger workloads
- server deployments

Characteristics:
- canonical files on shared filesystem or synced storage
- Postgres for runtime indexing/caching
- optional pgvector later
- better concurrency and querying

### 6.3 Shared requirement across both modes
These must remain identical:
- project format
- file structure
- schema definitions
- agent specs
- templates
- import/export flow
- rebuild logic
- file semantics

## 7. Canonical project structure

Base structure:

```text
.agctor/
  project.yaml
  runtime.yaml

  agents/
    shared/
    <project-type>/

  schemas/
    common/
    <project-type>/

  templates/
    <project-type>/

  <entity-roots>/
  views/
  runtime/
  logs/
```

### 7.1 Example People project

```text
.agctor/
  project.yaml
  runtime.yaml

  agents/
    shared/
      memory-curator.agent.yaml
      query-agent.agent.yaml
    people/
      person-extractor.agent.yaml
      person-summarizer.agent.yaml
      relationship-analyst.agent.yaml

  schemas/
    common/
      entities.schema.yaml
      documents.schema.yaml
      routing.schema.yaml
    people/
      project-type.yaml
      entity-types.yaml
      document-types.yaml
      routing-rules.yaml
      workspace-schema.yaml

  templates/
    people/
      entity.template.yaml
      profile.template.md
      skills.template.md
      relationships.template.md
      timeline.template.md

  people/
    raha/
      entity.yaml
      profile.md
      skills.md
      relationships.md
      timeline.md
    ryan/
      entity.yaml
      profile.md
      relationships.md
      timeline.md

  views/
    people-index.md
    relationship-map.md

  runtime/
    sqlite/
      agctor.db
    cache/
      .gitkeep

  logs/
    rebuilds/
    imports/
```

## 8. What goes under `agents/`

The `agents/` folder contains portable agent role specifications.

Each `*.agent.yaml` must define:
- id
- name
- role
- description
- compatible project types
- instructions
- input contract
- output contract
- tools allow/deny
- memory/file access
- guardrails
- optional tool bundles
- optional runtime hints

### 8.1 Agent schema

```yaml
id: string
name: string
role: string
description: string

projectTypes:
  - string

toolBundles:
  - string

instructions:
  - string

input:
  type: string

output:
  type: string

tools:
  allow:
    - string
  deny:
    - string

memoryAccess:
  read:
    - string
  write:
    - string

guardrails:
  - string

runtimeHints:
  preferredModel: string
  preferredMode: sqlite|postgres|any
```

### 8.2 Example: `agents/people/person-extractor.agent.yaml`

```yaml
id: person-extractor
name: Person Extractor
role: extraction
description: Extracts person-related facts, relationships, events, and observations from text.

projectTypes:
  - people

instructions:
  - Identify person entities mentioned in the input.
  - Extract only supported knowledge types.
  - Distinguish facts, relationships, events, and observations.
  - Produce memory intents, not direct file writes.
  - Keep confidence conservative.

input:
  type: text_or_document

output:
  type: memory_intents_json

tools:
  allow:
    - read_document
    - search_entities
    - classify_fact
  deny:
    - write_document
    - arbitrary_path_write

memoryAccess:
  read:
    - people/*/entity.yaml
    - people/*/profile.md
    - people/*/relationships.md
  write:
    - memory_intents_only

guardrails:
  - Do not invent missing facts.
  - Mark uncertainty when confidence is low.
  - Prefer atomic memory intents.
```

### 8.3 Example: `agents/shared/memory-curator.agent.yaml`

```yaml
id: memory-curator
name: Memory Curator
role: curation
description: Validates memory intents and updates canonical files according to schema rules.

projectTypes:
  - people
  - sales
  - jobs
  - software

instructions:
  - Load project schema and routing rules.
  - Validate entity identity and allowed knowledge types.
  - Route memory intents to allowed target documents.
  - Preserve markdown template structure.
  - Avoid duplicate entries.

input:
  type: memory_intents_json

output:
  type: document_update_plan

tools:
  allow:
    - read_document
    - write_document
    - load_schema
    - update_index
  deny:
    - arbitrary_path_write

memoryAccess:
  read:
    - schemas/**
    - templates/**
    - people/**
    - sales/**
    - jobs/**
  write:
    - schema_allowed_targets_only

guardrails:
  - Never write outside schema-defined paths.
  - Never delete canonical documents without explicit policy.
  - Preserve human-readable formatting.
```

## 9. What goes under `schemas/`

The `schemas/` folder contains the declarative rules that define the project’s structure and persistence behavior.

### 9.1 Required schema files
Each project type should define:
- `project-type.yaml`
- `entity-types.yaml`
- `document-types.yaml`
- `routing-rules.yaml`
- `workspace-schema.yaml`

### 9.2 `project-type.yaml`
Defines:
- project type name
- version
- supported entity types
- supported document types
- references to other schema files

Example:

```yaml
projectType: people
version: 1
displayName: People Project

entityTypes:
  - person

documentTypes:
  - profile
  - skills
  - relationships
  - timeline

workspaceSchemaRef: schemas/people/workspace-schema.yaml
routingRulesRef: schemas/people/routing-rules.yaml
```

### 9.3 `entity-types.yaml`
Defines entity folder patterns and required docs.

Example:

```yaml
entityTypes:
  - id: person
    folderPattern: "people/{entityKey}/"
    metadataFile: "entity.yaml"
    keyStrategy: slug_name
    requiredDocuments:
      - profile.md
      - relationships.md
    optionalDocuments:
      - skills.md
      - timeline.md
```

### 9.4 `document-types.yaml`
Defines:
- document id
- filename
- purpose
- update mode
- sections

Example:

```yaml
documentTypes:
  - id: profile
    fileName: profile.md
    purpose: Stable profile information
    updateMode: replace_section
    sections:
      - Basic Info
      - Physical Attributes
      - Roles
      - Notes

  - id: skills
    fileName: skills.md
    purpose: Skills and competencies
    updateMode: merge_list
    sections:
      - Technical Skills
      - Professional Skills
      - Other Skills

  - id: relationships
    fileName: relationships.md
    purpose: Person relationships
    updateMode: merge_list
    sections:
      - Family
      - Friends
      - Professional
      - Romantic

  - id: timeline
    fileName: timeline.md
    purpose: Dated events and observations
    updateMode: append_chronological
    sections:
      - Events
      - Changes
      - Observations
```

### 9.5 `routing-rules.yaml`
Maps knowledge types to docs and sections.

Example:

```yaml
routingRules:
  - when:
      knowledgeType: occupation
    target:
      document: profile
      section: Basic Info

  - when:
      knowledgeType: physical_attribute
      attribute: eye_color
    target:
      document: profile
      section: Physical Attributes

  - when:
      knowledgeType: skill
    target:
      document: skills
      section: Technical Skills

  - when:
      knowledgeType: family_role
    target:
      document: relationships
      section: Family

  - when:
      knowledgeType: event
    target:
      document: timeline
      section: Events

  - when:
      knowledgeType: observation
    target:
      document: timeline
      section: Observations
```

### 9.6 `workspace-schema.yaml`
Defines high-level folder grammar and view rules.

Example:

```yaml
workspace:
  roots:
    - people/
    - views/

  entityViews:
    - entityType: person
      folderPattern: "people/{entityKey}/"
      documents:
        - profile.md
        - skills.md
        - relationships.md
        - timeline.md

  indexViews:
    - path: views/people-index.md
      purpose: Summary of all people
```

## 10. What goes under `templates/`

Templates define default structure for canonical docs.

Examples:
- `profile.template.md`
- `skills.template.md`
- `relationships.template.md`
- `timeline.template.md`
- `entity.template.yaml`

Example `profile.template.md`:

```markdown
# {{displayName}} Profile

## Basic Info

## Physical Attributes

## Roles

## Notes
```

Templates must be used when:
- creating a new entity
- creating missing documents
- rebuilding missing documents if permitted by policy

## 11. Entity folder contract

Each canonical entity folder should contain:
- `entity.yaml`
- one or more markdown docs defined by schema

Example `people/raha/entity.yaml`:

```yaml
entityKey: raha
entityType: person
displayName: Raha
aliases:
  - Raha Mohebbi
schemaVersion: 1
status: active
```

Example `people/raha/profile.md`:

```markdown
# Raha Profile

## Basic Info
- Occupation: Software Engineer

## Physical Attributes
- Eye color: Green

## Roles
- Father

## Notes
- Prefers portable project structures.
```

## 12. Memory model

### 12.1 Canonical truth
Canonical truth lives in:
- markdown docs
- YAML manifests
- entity metadata
- schema definitions
- agent definitions

### 12.2 Lightweight structured runtime layer
The runtime may maintain a small structured layer for:
- entity lookup
- routing
- section indexing
- duplicate detection
- change tracking
- search acceleration

This layer is rebuildable.

### 12.3 Memory intents
Agents should produce memory intents rather than arbitrary file edits.

Example:

```json
{
  "memoryIntents": [
    {
      "entityKey": "raha",
      "knowledgeType": "occupation",
      "value": "software engineer",
      "confidence": 0.95
    },
    {
      "entityKey": "raha",
      "knowledgeType": "family_role",
      "value": "father",
      "confidence": 0.98
    },
    {
      "entityKey": "raha",
      "knowledgeType": "physical_attribute",
      "attribute": "eye_color",
      "value": "green",
      "confidence": 0.92
    }
  ]
}
```

### 12.4 Memory pipeline
1. input text/document arrives
2. extractor agent emits memory intents
3. memory curator validates intents
4. routing rules determine target docs/sections
5. canonical markdown files are updated
6. runtime index is refreshed

## 13. Rebuild architecture

### 13.1 Goal
A project must be reconstructible from project files alone.

### 13.2 Rebuild sources
Rebuild must use:
- `project.yaml`
- schema files
- agent files
- entity folders
- markdown documents
- templates where necessary

### 13.3 Rebuild outputs
Rebuild recreates:
- entity registry
- document index
- parsed section map
- routing cache
- optional embeddings later
- search index
- runtime DB

### 13.4 Rebuild flow
1. load `project.yaml`
2. load active runtime mode
3. load project schema
4. discover entity folders
5. parse `entity.yaml`
6. parse markdown docs
7. build section indexes
8. validate schema compliance
9. populate SQLite or Postgres
10. write rebuild logs

### 13.5 Validation rules
Rebuild must detect:
- missing required docs
- duplicate entity keys
- invalid schema references
- invalid document sections
- unsupported file placements
- broken routing targets

## 14. Portable project package

A project export/import package must include:
- `project.yaml`
- `agents/`
- `schemas/`
- `templates/`
- canonical entity folders
- views

A database backup must not be required.

It is acceptable to lose:
- caches
- embeddings
- runtime execution state
- temporary job queues
- denormalized indexes

It is not acceptable to lose:
- entities
- canonical docs
- schema rules
- agent definitions
- routing rules
- durable metadata

## 15. Suggested runtime storage usage

### 15.1 SQLite mode
Use SQLite for:
- entity registry
- document index
- routing cache
- parsed section index
- local search support
- optional local lightweight task state

### 15.2 Postgres mode
Use Postgres for:
- shared entity registry
- shared document index
- routing cache
- larger search/index workloads
- concurrent runtime state
- optional pgvector later

### 15.3 Future: semantic retrieval
Semantic retrieval can be added later using:
- SQLite mode: local embedding cache or file-based index
- Postgres mode: pgvector

This must remain optional and secondary to canonical file truth.

## 16. Project types to support initially

### 16.1 People Project
Use case:
- extract, track, and query information about people

Canonical docs per person:
- `entity.yaml`
- `profile.md`
- `skills.md`
- `relationships.md`
- `timeline.md`

Agents:
- person-extractor
- relationship-analyst
- person-summarizer
- memory-curator
- query-agent

### 16.2 Sales Lead Project
Use case:
- find businesses
- qualify leads
- manage outreach
- track communication

Recommended canonical structure:

```text
sales/
  businesses/
    abc-construction/
      entity.yaml
      profile.md
      qualification.md
      communication.md
      outreach.md
  views/
    cities/
      irvine.md
      orange.md
    types/
      construction.md
    pipeline/
      hot-leads.md
      contacted.md
      follow-up.md
```

Agents:
- market-scout-agent
- lead-qualifier-agent
- outreach-strategist-agent
- communication-agent
- follow-up-agent
- memory-curator

### 16.3 Job Search Project
Use case:
- find roles
- tailor resumes
- apply
- track communication

Recommended canonical structure:

```text
jobs/
  companies/
    xyz-company/
      entity.yaml
      profile.md
      communication.md

  applications/
    xyz-company-senior-backend-engineer/
      entity.yaml
      role.md
      fit-analysis.md
      resume.md
      cover-letter.md
      application.md

  views/
    searches/
      2026-03-15-backend-dotnet-oc.md
    pipeline/
      applied.md
      interviewing.md
      waiting.md
```

Agents:
- job-discovery-agent
- fit-analysis-agent
- resume-tailor-agent
- cover-letter-agent
- application-agent
- communication-agent
- memory-curator

## 17. Canonical entity + multiple views rule

AGCTOR should prefer:
- one canonical entity folder
- multiple human-readable views

This avoids duplication and allows the same entity to appear in:
- a city view
- a type view
- a pipeline view
- a campaign view

Example:
- canonical: `sales/businesses/abc-construction/`
- views:
  - `views/cities/irvine.md`
  - `views/types/construction.md`
  - `views/pipeline/hot-leads.md`

## 18. UX requirements

### 18.1 Project creation UX
The user must be able to:
- create a project
- choose project type
- choose runtime mode
- inspect project schema
- create entities
- create agents from templates

### 18.2 Agent Studio
AGCTOR should provide an interface to create/edit agents with sections for:
- overview
- instructions
- tools
- memory/file access
- input/output contracts
- project compatibility
- guardrails
- runtime hints

### 18.3 Schema Studio
AGCTOR should provide an interface to define:
- project type
- entity types
- document types
- update modes
- routing rules
- workspace schema
- view definitions

### 18.4 File explorer / workspace browser
The user must be able to browse:
- canonical entities
- views
- agent definitions
- schema definitions
- templates

### 18.5 Rebuild/import UX
The user must be able to:
- import a project folder
- validate it
- rebuild runtime indexes
- see errors/warnings
- continue working without DB backup

## 19. Tooling model

Agents must not be configured with unrestricted raw access by default.

The system must support:
- allow list of tools
- deny list of tools
- file access scope
- schema-aware write restrictions

Future enhancement:
- tool bundles, for example:
  - people-extraction
  - memory-curation
  - semantic-query
  - sales-outreach
  - resume-authoring

## 20. Update modes

Document types must support explicit update modes.

Required initial update modes:
- `replace_section`
- `merge_list`
- `append_chronological`

Examples:
- `profile.md` uses `replace_section`
- `skills.md` uses `merge_list`
- `timeline.md` uses `append_chronological`

The memory curator must implement these modes deterministically.

## 21. APIs / internal services

Cursor implementation should create internal services or modules for:

### 21.1 ProjectLoader
Responsibilities:
- load `project.yaml`
- resolve paths
- load runtime mode
- validate project structure

### 21.2 SchemaRegistry
Responsibilities:
- load schemas
- validate schema references
- expose entity/doc/routing definitions

### 21.3 AgentRegistry
Responsibilities:
- discover agent specs
- validate agent contracts
- expose agents by project type and role

### 21.4 EntityRegistry
Responsibilities:
- discover entities
- parse `entity.yaml`
- index canonical entity paths

### 21.5 DocumentParser
Responsibilities:
- parse markdown sections
- validate templates/required sections
- provide structured section map

### 21.6 MemoryIntentProcessor
Responsibilities:
- accept memory intents
- validate against schema
- normalize/update route targets

### 21.7 DocumentProjectionService
Responsibilities:
- update markdown docs using schema update modes
- preserve formatting and headings

### 21.8 RuntimeIndexBuilder
Responsibilities:
- rebuild SQLite/Postgres index from files
- update caches
- refresh runtime state

### 21.9 RebuildCoordinator
Responsibilities:
- run full rebuild workflow
- surface errors and warnings
- log rebuild operations

## 22. Suggested implementation language and boundaries

Since AGCTOR is being built in .NET/C#, implement the initial MVP in C# with clear abstractions around:
- filesystem
- schema loading
- runtime DB provider
- markdown parsing
- agent definitions
- rebuild pipeline

Suggested boundaries:
- `IAgentDefinitionProvider`
- `ISchemaProvider`
- `IProjectLoader`
- `IDocumentParser`
- `IMemoryIntentProcessor`
- `IDocumentProjectionService`
- `IRuntimeIndexStore`
- `IRebuildCoordinator`

Provide runtime implementations for:
- `SQLiteRuntimeIndexStore`
- `PostgresRuntimeIndexStore`

## 23. Acceptance criteria

### 23.1 Core project criteria
1. A project can be created from files alone.
2. A project can be restored from files alone.
3. No database backup is required for portability.
4. Same project works in SQLite and Postgres modes.

### 23.2 Agent criteria
1. Agents are defined by YAML config.
2. Agents declare tools and file access.
3. Agents are validated against project type compatibility.
4. Agents do not perform arbitrary writes outside schema rules.

### 23.3 Schema criteria
1. Project type schema defines entity types, docs, and routing.
2. Document update modes are enforced.
3. Routing rules control where information is written.
4. Missing required docs can be detected and optionally templated.

### 23.4 Memory criteria
1. Extractor agent can emit memory intents.
2. Memory curator can apply memory intents to canonical docs.
3. Runtime indexes can be rebuilt from files.
4. Canonical docs remain human-readable.

### 23.5 Mode criteria
1. SQLite mode works locally without server DB.
2. Postgres mode works with shared runtime index.
3. Project import/export does not depend on runtime DB backup.

## 24. MVP scope

Implement first:

### 24.1 Core infrastructure
- project loader
- schema registry
- agent registry
- entity discovery
- markdown parser
- runtime rebuild pipeline
- SQLite mode
- Postgres mode interface, optional first implementation shortly after

### 24.2 Project type
Implement first full project type:
- People Project

### 24.3 Agents
Implement first agents:
- person-extractor
- memory-curator
- person-query-agent

### 24.4 Update modes
Implement:
- replace_section
- merge_list
- append_chronological

### 24.5 Import/rebuild
Implement:
- create from template
- rebuild runtime from files
- validate and report errors

## 25. Phase 2 scope

1. Sales Lead Project schema
2. Job Search Project schema
3. View generation helpers
4. Tool bundles
5. richer validation
6. optional semantic retrieval
7. agent studio UI
8. schema studio UI

## 26. Risks and mitigations

### Risk: markdown becomes messy
Mitigation:
- templates
- explicit sections
- update modes
- curator-controlled writes

### Risk: hidden semantics live only in DB
Mitigation:
- keep durable semantics in files
- use DB only for rebuildable acceleration

### Risk: schemas become too loose
Mitigation:
- strict validation
- schema versioning
- required docs and allowed paths

### Risk: agents write unpredictably
Mitigation:
- memory intents
- curator agent
- schema-enforced routing

### Risk: top-level summary files become bloated
Mitigation:
- canonical entities + multiple views
- do not overload summary docs as sole truth

## 27. Final recommended default file layout for MVP

```text
.agctor/
  project.yaml
  runtime.yaml

  agents/
    shared/
      memory-curator.agent.yaml
    people/
      person-extractor.agent.yaml
      person-query.agent.yaml

  schemas/
    people/
      project-type.yaml
      entity-types.yaml
      document-types.yaml
      routing-rules.yaml
      workspace-schema.yaml

  templates/
    people/
      entity.template.yaml
      profile.template.md
      skills.template.md
      relationships.template.md
      timeline.template.md

  people/
    raha/
      entity.yaml
      profile.md
      skills.md
      relationships.md
      timeline.md

  views/
    people-index.md

  runtime/
    sqlite/
      agctor.db
```

## 28. Final architectural statement

AGCTOR will use a **file-canonical, markdown-first, schema-driven architecture** where:
- projects define workspace and memory structure
- agents are configurable and portable
- canonical truth lives in portable files
- runtime databases are rebuildable acceleration layers
- SQLite supports local/lightweight mode
- Postgres supports shared/high-performance mode
- the same project package works across both

This is the target design Cursor should implement.
