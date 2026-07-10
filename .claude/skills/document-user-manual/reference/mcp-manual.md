# MCP user-manual skeleton

The section order for a production MCP-server manual. **System-agnostic** — fill from the real
`tools/list` and each tool's schema. An MCP manual differs from a CLI manual in three ways it must
address: the **client setup** (the reader configures a host, not a shell), the **tool surface**
(schemas, not flags), and **state across calls** (one session, many tool calls).

```
# <Product> MCP Server — User Manual

## 1. Overview            — what the server exposes, to which clients (Claude Desktop, IDEs, agents)
## 2. Install & connect   — install the server; the client config (.mcp.json / host settings);
                            confirm the handshake + that tools appear
## 3. Concepts            — domain terms (from CONTEXT.md); the session/state model — what persists
                            across tool calls within one connection vs across reconnects

## — LIFECYCLE (as tool calls) —
## 4. Create              — the tool(s) that create a base/workspace; arguments + result
## 5. Import / ingest     — ingest tools; pointer vs embed modes if any
## 6. Align / configure   — tag/index/configure tools
## 7. Operate             — the write/action tools, grouped by task
## 8. Query / retrieve    — the read tools; real call + real JSON result
## 9. Maintain            — compact/sync/snapshot tools; switching the active base mid-session

## — PRODUCTION —
## 10. Modes & tiers       — per-mode tool availability (a tool refused in one mode) (if applicable)
## 11. Security            — auth, encryption, what the server will/won't do over the wire
## 12. Audit & compliance  — audit tools; how a tool call is recorded (if applicable)
## 13. Access control      — per-tool/role gating (if applicable)
## 14. Backup & restore    — backup/restore via tools or out-of-band
## 15. Deployment          — running the server in production; transport; supervision
## 16. Configuration       — every config key + env var: name · default · location · effect

## — SAFETY NET —
## 17. Troubleshooting     — symptom → cause → fix (handshake fails, tool not found, schema rejected)
## 18. Errors & results    — the result/error envelope shape; what a clean failure looks like
## 19. Glossary            — domain terms (reuse the project's ubiquitous language)
## 20. Tool quick-reference — every tool, one line each, on one screen
```

## Per-tool entry shape

```
### <tool_name>
<one-line purpose, in the user's task language>

Arguments:  <name> (<type>, required?) — <meaning>   ← every arg, from the real input schema
Read-only:  yes/no                                   ← does it mutate state?
Example:    call <tool_name> { "<arg>": <value> }
Result:     <the actual JSON result excerpt / the side effect on state>
Errors:     <how it fails on bad args; what a clean error looks like>
Notes:      <mode interactions; sequencing with other tools>
```

## Rules

- **Tools and schemas come from the real `tools/list`**, not memory — reconcile against the docs and
  document any discrepancy.
- **Show a multi-call sequence on one session** (e.g. create → import → query) so the reader sees state
  persisting across calls — the thing CLI docs don't have.
- **Document the client config concretely** (a real `.mcp.json` / host-settings block) — a reader can't
  use a single tool until the server connects.
- **Group tools by task** in Operate/Query; alphabetical lives only in §20.
