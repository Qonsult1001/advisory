/* tslint:disable */
/* eslint-disable */

export class SaidBrain {
    free(): void;
    [Symbol.dispose](): void;
    /**
     * Semantic + lexical fused recall — uses the static encoder.
     */
    ask(query: string, k: number): any;
    /**
     * Run the full 3-engine fused ask. Mirrors `sca_core::ask::ask()`.
     * `deep` enables the long-passage rerank engine when available.
     */
    ask_fused(query: string, top: number, deep: boolean): any;
    /**
     * Checkout — revive an old version by frame_id, making it the current
     * active frame. Returns the new frame_id + semantic delta from prior.
     */
    checkout(frame_id: bigint): any;
    /**
     * Checkout by version index — mirrors CLI `said checkout NAME --version V`.
     * Walks the lineage and resolves the V-th frame_id.
     */
    checkout_by_version(doc_id: string, version: number): any;
    /**
     * Compact — drop tombstoned frames, reclaim bytes. Returns counts.
     * Mirrors CLI `said compact` (no flags).
     */
    compact(): any;
    /**
     * Full `said compact --drop-history [--all | --keep N]` semantics.
     * Same guardrails as CLI: --drop-history requires either --all OR
     * --keep N; --all + --keep is rejected.
     */
    compact_with_options(drop_history: boolean, all: boolean, keep_per_doc?: number | null): any;
    /**
     * Decay recall weights on cold (unused) docs. Mirrors the CLI's
     * auto-consolidation step inside `compact`. Returns # docs decayed.
     */
    consolidate(): number;
    /**
     * Total active-memory count — for paginator math.
     */
    count_memories(): number;
    /**
     * Soft-delete a memory (tombstone — recoverable via `restore`).
     */
    delete(doc_id: string): boolean;
    /**
     * Discover — aggregate report: sources, pillar counts, type counts, top tags.
     * Mirrors the CLI's `said discover`.
     */
    discover(): any;
    /**
     * Dream — run a consolidation cycle if the brain has accumulated
     * enough pending queries. Returns whether a cycle fired.
     * Mirrors the auto-dream path the CLI fires after `said ask`.
     */
    dream(min_queries: bigint): boolean;
    /**
     * Drop ALL tombstones (no-keep). Use only after explicit user confirm.
     */
    drop_history(): number;
    /**
     * Drop tombstones older than the N most recent per doc_id.
     */
    drop_history_keep(keep_per_doc: number): number;
    /**
     * Replace the content of an existing memory (creates a new version,
     * keeping the old one in the lineage). Returns the new frame id.
     */
    edit(doc_id: string, content: string, title?: string | null): bigint;
    /**
     * Full content + metadata for a single memory.
     */
    get_memory(doc_id: string): any;
    /**
     * History tab — append-only audit log, newest first.
     */
    history(limit: number): any;
    /**
     * Bulk-import: each (doc_id, content) pair becomes a memory.
     * Mirrors a subset of CLI `said import` (text/JSON path only).
     */
    import_text(doc_ids: any[], contents: any[], titles: any[]): number;
    /**
     * Stream 1: extract a DOCX's text and index it as searchable memories.
     * Returns the number of paragraph segments indexed. Available to all brains.
     */
    ingest_document_search(bytes: Uint8Array, filename: string): number;
    /**
     * Add a doc_id to legal hold under a case_id. Returns count of frames
     * now under hold.
     */
    legal_hold_add(doc_id: string, case_id: string): number;
    /**
     * Release a doc_id from legal hold for a case_id.
     */
    legal_hold_release(doc_id: string, case_id: string): number;
    /**
     * Lineage of a doc_id — chronological list of all versions.
     */
    lineage(doc_id: string): any;
    /**
     * List active memories with previews. `offset`/`limit` paginate.
     */
    list_memories(offset: number, limit: number): any;
    /**
     * Load a `.said` file from in-memory bytes.
     *
     * `bytes` — the raw file contents from the browser's File API
     * `filename` — display name + default for the save-as-download flow
     * `encoder_*` — the three blobs (tokenizer.json, model.safetensors,
     *   config.json). Pass empty `Uint8Array`s to skip the encoder load
     *   (search degrades to lexical-only).
     */
    constructor(bytes: Uint8Array, filename: string, encoder_tokenizer: Uint8Array, encoder_safetensors: Uint8Array, encoder_config: Uint8Array);
    /**
     * Rebuild the search index after edits. Run before `save_to_bytes`.
     */
    rebuild_index(): void;
    /**
     * Add a new memory.
     */
    remember(content: string, title?: string | null): bigint;
    /**
     * Generic pillar-aware remember. Useful for distillation flows
     * (the LLM decides "this is a fact, write to Semantic").
     */
    remember_with_pillar(content: string, pillar: string, doc_id: string | null | undefined, title: string | null | undefined, tags: any[]): bigint;
    /**
     * Restore a tombstoned memory.
     */
    restore(doc_id: string): void;
    /**
     * Salience score — pure scorer (no mutation). Same as MCP `salience`.
     * Returns score (0-100), band (low|medium|high), and tag list. Used
     * before `remember` to decide whether content is worth keeping.
     */
    salience_score(text: string, pillar?: string | null): any;
    /**
     * Serialize the (possibly edited) brain back to a Vec<u8>. The JS
     * side wraps this in a Blob and triggers a download.
     */
    save_to_bytes(): Uint8Array;
    /**
     * Lexical grep — fast keyword search with snippets.
     */
    search(query: string, k: number): any;
    /**
     * List Stream-1 (search) source documents as [{filename, segments}].
     * Available to ALL brains — this is the visible record that a search
     * ingest landed, independent of the Enterprise-only vault.
     */
    search_document_list(): any;
    /**
     * Remove a search-only (Stream 1) document from the index by filename.
     * Deletes every `{base}::para_N` frame for that document. Available to ALL
     * brains (search is not enterprise-only). Returns the number of segments
     * removed. Does NOT touch the vault — a doc also stored in the vault keeps
     * its vault copy (erase it from the Vault section for that).
     */
    search_document_remove(filename: string): number;
    /**
     * Write an Episodic frame summarising one full conversation turn.
     * Mirrors the MCP `session_end` tool: tagged `event:session_end`
     * + optional `session:<id>` + caller tags. Default doc_id is
     * `ep_session_<id>` when session_id is provided, or auto `mem_N`.
     * Returns the new frame_id.
     */
    session_end(summary: string, session_id: string | null | undefined, tags: any[]): bigint;
    /**
     * Distinct sources currently present (parent file paths or collection IDs).
     */
    sources(): any;
    /**
     * Stats panel — single call returns everything the dashboard needs.
     */
    stats(): any;
    /**
     * Symbol search — exact-name lookup against the symbol index.
     */
    sym(name: string, max: number): any;
    /**
     * Symbol prefix list — used for "find by partial name".
     */
    sym_list(prefix: string, max: number): any;
    /**
     * Total number of symbols indexed across all active frames.
     */
    symbol_count(): number;
    /**
     * Tombstones tab — Recycle Bin view of soft-deleted memories.
     */
    tombstones(): any;
    /**
     * Write an Episodic frame for a single tool invocation. Mirrors the
     * MCP `tool_completion` tool. Recurring tool+args+result patterns
     * distill to Procedural at the next dream cycle.
     */
    tool_completion(tool_name: string, body: string, tags: any[]): bigint;
    /**
     * Read the vault audit log as [{seq, action, doc_id, target, actor, at,
     * prev_hash, note}], oldest first. ENTERPRISE.
     */
    vault_audit(): any;
    /**
     * Append a vault audit event from the UI (e.g. a "view" action that has no
     * other binding). ENTERPRISE.
     */
    vault_audit_log(action: string, doc_id: string, note: string): void;
    /**
     * Verify the audit hash-chain integrity. Returns
     * `{ok, count, broken_at, detail}`. ENTERPRISE.
     */
    vault_audit_verify(): any;
    /**
     * Whether a document may be deleted right now: `{ok, reason}`. ENTERPRISE.
     */
    vault_deletable(doc_id: string): any;
    /**
     * Erase a vault document (GDPR Art. 17). Refused if under legal hold or
     * before its disposition date. Removes the manifest, retention record,
     * byte-exact tombstone, and any dedup assets not shared with other docs;
     * records a `delete` audit event. Returns a tamper-evident deletion
     * certificate `{doc_id, filename, original_blake3, deleted_by, deleted_at,
     * reason, assets_removed, tombstone_removed, seal}`. ENTERPRISE.
     */
    vault_delete(doc_id: string, reason: string): any;
    /**
     * Stream 2 (ENTERPRISE): store a DOCX in the vault. legal=true keeps the
     * byte-exact tombstone; false is slim. Returns the doc_id.
     */
    vault_ingest(bytes: Uint8Array, filename: string, legal: boolean): string;
    /**
     * List vault documents with full manifest provenance. ENTERPRISE.
     *
     * Returns one row per document with everything the Manifest holds:
     * `{doc_id, filename, tier, size_bytes, format, ingested_at, ingested_by,
     *   tags, part_count, blake3}`. The UI uses this for sortable columns,
     * tag facets, the detail drawer, and the integrity surface.
     */
    vault_list(): any;
    /**
     * Rebuild a document from dedup parts (structural). Returns the bytes. ENTERPRISE.
     */
    vault_rebuild(doc_id: string): Uint8Array;
    /**
     * Restore the byte-exact original (legal only). Returns the bytes. ENTERPRISE.
     */
    vault_restore(doc_id: string): Uint8Array;
    /**
     * Read a document's retention record as
     * `{doc_id, class, disposition_at, legal_hold, reason, updated_at}`, or
     * null if none is set. ENTERPRISE.
     */
    vault_retention_get(doc_id: string): any;
    /**
     * Place or release a legal hold. A held document cannot be deleted or
     * compacted. ENTERPRISE.
     */
    vault_set_hold(doc_id: string, hold: boolean, reason: string): void;
    /**
     * Set a document's retention class + disposition date (unix seconds, 0 =
     * none). Preserves any existing legal hold. ENTERPRISE.
     */
    vault_set_retention(doc_id: string, _class: string, disposition_at: number, reason: string): void;
    /**
     * Vault-wide dedup + storage savings. ENTERPRISE.
     *
     * Walks every manifest's zip-entry asset hashes: `total_parts` counts all
     * part references across documents; `unique_parts` counts distinct asset
     * hashes actually stored. The difference is what content-addressed dedup
     * saved. `raw_bytes` is the sum of original document sizes. Returns
     * `{doc_count, total_parts, unique_parts, shared_parts, raw_bytes,
     *   legal_count, slim_count}`.
     */
    vault_stats(): any;
    /**
     * Verify a legal document restores byte-identical to the original.
     * ENTERPRISE.
     *
     * Restores the byte-exact original, re-hashes it with BLAKE3, and compares
     * to the tombstone hash recorded at ingest. Returns
     * `{verified, computed, expected, size_bytes}` — `verified=true` means the
     * restored bytes are provably identical to what was ingested. This turns
     * the product's central byte-exact claim from asserted into demonstrated.
     * Errors for slim documents (no tombstone to verify against).
     */
    vault_verify(doc_id: string): any;
    /**
     * Filename the user dropped — used by save-as-download.
     */
    readonly filename: string;
    /**
     * Brain mode (Portable / Enterprise). Read-only — set at create time.
     */
    readonly mode: string;
}

export function _start(): void;

/**
 * Return the list of available agent prompts as JSON. Mirrors the MCP
 * `prompts/list` response shape so a thin JS wrapper can present them
 * in a UI menu identically to MCP clients (Claude Desktop, Cursor).
 */
export function list_prompts(): any;

/**
 * Build the system prompt for a given agent role. Single source of
 * truth lives in the `said-prompts` crate; the WASM agent loop calls
 * this once at session start instead of inlining ~250 lines of text
 * in JS.
 *
 * `role` — `"answerer"` (default). Future: `"searcher"`, `"writer"`,
 * `"compiler"`. Unknown roles fall back to `answerer`.
 * `files_json` — JSON array of loaded brain filenames (e.g.
 * `["willie.said", "brain.said"]`).
 * `total_docs` — sum of active memories across loaded brains.
 */
export function system_prompt(role: string, files_json: string, total_docs: number): string;

export type InitInput = RequestInfo | URL | Response | BufferSource | WebAssembly.Module;

export interface InitOutput {
    readonly memory: WebAssembly.Memory;
    readonly __wbg_saidbrain_free: (a: number, b: number) => void;
    readonly _start: () => void;
    readonly list_prompts: () => [number, number, number];
    readonly saidbrain_ask: (a: number, b: number, c: number, d: number) => [number, number, number];
    readonly saidbrain_ask_fused: (a: number, b: number, c: number, d: number, e: number) => [number, number, number];
    readonly saidbrain_checkout: (a: number, b: bigint) => [number, number, number];
    readonly saidbrain_checkout_by_version: (a: number, b: number, c: number, d: number) => [number, number, number];
    readonly saidbrain_compact: (a: number) => [number, number, number];
    readonly saidbrain_compact_with_options: (a: number, b: number, c: number, d: number) => [number, number, number];
    readonly saidbrain_consolidate: (a: number) => number;
    readonly saidbrain_count_memories: (a: number) => number;
    readonly saidbrain_delete: (a: number, b: number, c: number) => number;
    readonly saidbrain_discover: (a: number) => [number, number, number];
    readonly saidbrain_dream: (a: number, b: bigint) => number;
    readonly saidbrain_drop_history: (a: number) => number;
    readonly saidbrain_drop_history_keep: (a: number, b: number) => number;
    readonly saidbrain_edit: (a: number, b: number, c: number, d: number, e: number, f: number, g: number) => bigint;
    readonly saidbrain_filename: (a: number) => [number, number];
    readonly saidbrain_get_memory: (a: number, b: number, c: number) => [number, number, number];
    readonly saidbrain_history: (a: number, b: number) => [number, number, number];
    readonly saidbrain_import_text: (a: number, b: number, c: number, d: number, e: number, f: number, g: number) => [number, number, number];
    readonly saidbrain_ingest_document_search: (a: number, b: number, c: number, d: number, e: number) => [number, number, number];
    readonly saidbrain_legal_hold_add: (a: number, b: number, c: number, d: number, e: number) => number;
    readonly saidbrain_legal_hold_release: (a: number, b: number, c: number, d: number, e: number) => number;
    readonly saidbrain_lineage: (a: number, b: number, c: number) => [number, number, number];
    readonly saidbrain_list_memories: (a: number, b: number, c: number) => [number, number, number];
    readonly saidbrain_mode: (a: number) => [number, number];
    readonly saidbrain_new: (a: number, b: number, c: number, d: number, e: number, f: number, g: number, h: number, i: number, j: number) => [number, number, number];
    readonly saidbrain_rebuild_index: (a: number) => [number, number];
    readonly saidbrain_remember: (a: number, b: number, c: number, d: number, e: number) => bigint;
    readonly saidbrain_remember_with_pillar: (a: number, b: number, c: number, d: number, e: number, f: number, g: number, h: number, i: number, j: number, k: number) => [bigint, number, number];
    readonly saidbrain_restore: (a: number, b: number, c: number) => [number, number];
    readonly saidbrain_salience_score: (a: number, b: number, c: number, d: number, e: number) => [number, number, number];
    readonly saidbrain_save_to_bytes: (a: number) => [number, number, number, number];
    readonly saidbrain_search: (a: number, b: number, c: number, d: number) => [number, number, number];
    readonly saidbrain_search_document_list: (a: number) => [number, number, number];
    readonly saidbrain_search_document_remove: (a: number, b: number, c: number) => [number, number, number];
    readonly saidbrain_session_end: (a: number, b: number, c: number, d: number, e: number, f: number, g: number) => [bigint, number, number];
    readonly saidbrain_sources: (a: number) => [number, number, number];
    readonly saidbrain_stats: (a: number) => [number, number, number];
    readonly saidbrain_sym: (a: number, b: number, c: number, d: number) => [number, number, number];
    readonly saidbrain_sym_list: (a: number, b: number, c: number, d: number) => [number, number, number];
    readonly saidbrain_symbol_count: (a: number) => number;
    readonly saidbrain_tombstones: (a: number) => [number, number, number];
    readonly saidbrain_tool_completion: (a: number, b: number, c: number, d: number, e: number, f: number, g: number) => [bigint, number, number];
    readonly saidbrain_vault_audit: (a: number) => [number, number, number];
    readonly saidbrain_vault_audit_log: (a: number, b: number, c: number, d: number, e: number, f: number, g: number) => [number, number];
    readonly saidbrain_vault_audit_verify: (a: number) => [number, number, number];
    readonly saidbrain_vault_deletable: (a: number, b: number, c: number) => [number, number, number];
    readonly saidbrain_vault_delete: (a: number, b: number, c: number, d: number, e: number) => [number, number, number];
    readonly saidbrain_vault_ingest: (a: number, b: number, c: number, d: number, e: number, f: number) => [number, number, number, number];
    readonly saidbrain_vault_list: (a: number) => [number, number, number];
    readonly saidbrain_vault_rebuild: (a: number, b: number, c: number) => [number, number, number, number];
    readonly saidbrain_vault_restore: (a: number, b: number, c: number) => [number, number, number, number];
    readonly saidbrain_vault_retention_get: (a: number, b: number, c: number) => [number, number, number];
    readonly saidbrain_vault_set_hold: (a: number, b: number, c: number, d: number, e: number, f: number) => [number, number];
    readonly saidbrain_vault_set_retention: (a: number, b: number, c: number, d: number, e: number, f: number, g: number, h: number) => [number, number];
    readonly saidbrain_vault_stats: (a: number) => [number, number, number];
    readonly saidbrain_vault_verify: (a: number, b: number, c: number) => [number, number, number];
    readonly system_prompt: (a: number, b: number, c: number, d: number, e: number) => [number, number];
    readonly rust_zstd_wasm_shim_calloc: (a: number, b: number) => number;
    readonly rust_zstd_wasm_shim_free: (a: number) => void;
    readonly rust_zstd_wasm_shim_malloc: (a: number) => number;
    readonly rust_zstd_wasm_shim_memcmp: (a: number, b: number, c: number) => number;
    readonly rust_zstd_wasm_shim_memcpy: (a: number, b: number, c: number) => number;
    readonly rust_zstd_wasm_shim_memmove: (a: number, b: number, c: number) => number;
    readonly rust_zstd_wasm_shim_memset: (a: number, b: number, c: number) => number;
    readonly rust_zstd_wasm_shim_qsort: (a: number, b: number, c: number, d: number) => void;
    readonly __wbindgen_malloc: (a: number, b: number) => number;
    readonly __wbindgen_realloc: (a: number, b: number, c: number, d: number) => number;
    readonly __wbindgen_free: (a: number, b: number, c: number) => void;
    readonly __wbindgen_exn_store: (a: number) => void;
    readonly __externref_table_alloc: () => number;
    readonly __wbindgen_externrefs: WebAssembly.Table;
    readonly __externref_table_dealloc: (a: number) => void;
    readonly __wbindgen_start: () => void;
}

export type SyncInitInput = BufferSource | WebAssembly.Module;

/**
 * Instantiates the given `module`, which can either be bytes or
 * a precompiled `WebAssembly.Module`.
 *
 * @param {{ module: SyncInitInput }} module - Passing `SyncInitInput` directly is deprecated.
 *
 * @returns {InitOutput}
 */
export function initSync(module: { module: SyncInitInput } | SyncInitInput): InitOutput;

/**
 * If `module_or_path` is {RequestInfo} or {URL}, makes a request and
 * for everything else, calls `WebAssembly.instantiate` directly.
 *
 * @param {{ module_or_path: InitInput | Promise<InitInput> }} module_or_path - Passing `InitInput` directly is deprecated.
 *
 * @returns {Promise<InitOutput>}
 */
export default function __wbg_init (module_or_path?: { module_or_path: InitInput | Promise<InitInput> } | InitInput | Promise<InitInput>): Promise<InitOutput>;
