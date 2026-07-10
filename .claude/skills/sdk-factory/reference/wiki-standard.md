# SDK knowledge-wiki standard — the agent-native deliverable

The fourth deliverable: a **knowledge wiki** an LLM reads natively — the same files a human reads and
an agent parses, no SDK and no translation layer. The guides ([doc-standard.md](./doc-standard.md))
teach a *person* to integrate; the wiki gives an *agent* a navigable graph of the SDK's operations and
concepts it can consume from any filesystem, git repo, or catalogue. Ship it in **Open Knowledge
Format (OKF v0.1)** — Google's vendor-neutral spec for exactly this, so the wiki is portable and
globally interoperable, not a local-only convention.

Spec: <https://github.com/GoogleCloudPlatform/knowledge-catalog/tree/main/okf>.

## What OKF is, in one breath

A directory of **markdown files with YAML frontmatter**, cross-linked into a **graph**. "Just files,
just markdown, just YAML." All modern LLMs parse it with zero tooling. The whole v0.1 spec fits on a
page; the rules below are the conformance subset an SDK wiki must hold.

## Conformance rules (v0.1)

- **Every non-reserved `.md` file** begins with a YAML frontmatter block and that block carries a
  **non-empty `type`** field — the one required field (e.g. `type: SDK Operation`, `type: Concept`).
- **Recommended frontmatter**, in priority order: `title`, `description` (one sentence), `resource`
  (a URI identifying the underlying asset), `tags` (a YAML list), `timestamp` (ISO 8601). Producers
  may add custom fields; consumers must tolerate unknown ones.
- **Reserved filenames**: `index.md` (a directory listing for progressive disclosure) and `log.md`
  (chronological update history). These two **must not** be concept documents — no other reserved
  names exist.
- **Cross-links** form the graph. Prefer **bundle-relative** links (begin with `/`, resolved from the
  bundle root) — they survive file moves. Relative links are also allowed. A link asserts a
  relationship; the surrounding prose says what kind. Broken links are tolerated, not malformed — but
  an SDK wiki should resolve cleanly.

## The shape for an SDK wiki

Organize the bundle around the two things an agent asks of an SDK — *what can I call* and *what must I
understand*:

```
wiki/
├── index.md            # bundle root: what the SDK is + a map of the graph
├── log.md              # update history
├── operations/         # the callable surface — one doc per operation
│   ├── index.md        # grouped listing (retrieval / writing / lifecycle / …)
│   └── <operation>.md  # type: SDK Operation — params · returns · notes · links
├── concepts/           # the ideas a caller must grasp
│   ├── index.md
│   └── <concept>.md    # type: Concept — registration, the session/identity model,
│                       #   the cross-cutting transport, the result-parsing contract, the seam
└── guides/             # pointers INTO the human guides (don't duplicate them)
    ├── index.md
    └── <guide>.md      # type: Guide Pointer — one-paragraph hook + link to the real guide
```

Each **operation** doc carries the same facts the typed method exposes: parameters (name · type ·
required · note), the typed return, and links to the concepts it touches. Each **concept** doc is the
single home for one idea (cross-cutting, identity, pillars/classification, the parsing contract, the
adaptor/transport seam) — link to it from every operation that relies on it rather than restating it.
The **guides** folder *points at* the Consumer/Developer/Architecture docs; the wiki is the
agent-native graph, the guides are the human tutorials, and they cross-reference rather than copy.

## Trace, don't invent

Every operation doc traces to the same source of truth the client and mappers do (the spec / tool
definitions / handler responses). An operation you couldn't verify is flagged, not written. The wiki
inherits the recipe's cardinal rule: generate from the real scanned surface.

## Completion criterion

The wiki passes when: it OKF-conforms (every non-reserved file has a non-empty `type`; only `index.md`
and `log.md` are reserved), its cross-links resolve into a connected graph, every inventoried
operation has a document or an index entry, and an agent handed only the bundle can name an
operation's parameters and follow a link to the concept it depends on — without reading the SDK's
source.
