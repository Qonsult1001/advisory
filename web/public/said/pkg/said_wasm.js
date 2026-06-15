/* @ts-self-types="./said_wasm.d.ts" */

export class SaidBrain {
    __destroy_into_raw() {
        const ptr = this.__wbg_ptr;
        this.__wbg_ptr = 0;
        SaidBrainFinalization.unregister(this);
        return ptr;
    }
    free() {
        const ptr = this.__destroy_into_raw();
        wasm.__wbg_saidbrain_free(ptr, 0);
    }
    /**
     * Semantic + lexical fused recall — uses the static encoder.
     * @param {string} query
     * @param {number} k
     * @returns {any}
     */
    ask(query, k) {
        const ptr0 = passStringToWasm0(query, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_ask(this.__wbg_ptr, ptr0, len0, k);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Run the full 3-engine fused ask. Mirrors `sca_core::ask::ask()`.
     * `deep` enables the long-passage rerank engine when available.
     * @param {string} query
     * @param {number} top
     * @param {boolean} deep
     * @returns {any}
     */
    ask_fused(query, top, deep) {
        const ptr0 = passStringToWasm0(query, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_ask_fused(this.__wbg_ptr, ptr0, len0, top, deep);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Checkout — revive an old version by frame_id, making it the current
     * active frame. Returns the new frame_id + semantic delta from prior.
     * @param {bigint} frame_id
     * @returns {any}
     */
    checkout(frame_id) {
        const ret = wasm.saidbrain_checkout(this.__wbg_ptr, frame_id);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Checkout by version index — mirrors CLI `said checkout NAME --version V`.
     * Walks the lineage and resolves the V-th frame_id.
     * @param {string} doc_id
     * @param {number} version
     * @returns {any}
     */
    checkout_by_version(doc_id, version) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_checkout_by_version(this.__wbg_ptr, ptr0, len0, version);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Compact — drop tombstoned frames, reclaim bytes. Returns counts.
     * Mirrors CLI `said compact` (no flags).
     * @returns {any}
     */
    compact() {
        const ret = wasm.saidbrain_compact(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Full `said compact --drop-history [--all | --keep N]` semantics.
     * Same guardrails as CLI: --drop-history requires either --all OR
     * --keep N; --all + --keep is rejected.
     * @param {boolean} drop_history
     * @param {boolean} all
     * @param {number | null} [keep_per_doc]
     * @returns {any}
     */
    compact_with_options(drop_history, all, keep_per_doc) {
        const ret = wasm.saidbrain_compact_with_options(this.__wbg_ptr, drop_history, all, isLikeNone(keep_per_doc) ? 0x100000001 : (keep_per_doc) >>> 0);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Decay recall weights on cold (unused) docs. Mirrors the CLI's
     * auto-consolidation step inside `compact`. Returns # docs decayed.
     * @returns {number}
     */
    consolidate() {
        const ret = wasm.saidbrain_consolidate(this.__wbg_ptr);
        return ret >>> 0;
    }
    /**
     * Total active-memory count — for paginator math.
     * @returns {number}
     */
    count_memories() {
        const ret = wasm.saidbrain_count_memories(this.__wbg_ptr);
        return ret >>> 0;
    }
    /**
     * Soft-delete a memory (tombstone — recoverable via `restore`).
     * @param {string} doc_id
     * @returns {boolean}
     */
    delete(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_delete(this.__wbg_ptr, ptr0, len0);
        return ret !== 0;
    }
    /**
     * Discover — aggregate report: sources, pillar counts, type counts, top tags.
     * Mirrors the CLI's `said discover`.
     * @returns {any}
     */
    discover() {
        const ret = wasm.saidbrain_discover(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Dream — run a consolidation cycle if the brain has accumulated
     * enough pending queries. Returns whether a cycle fired.
     * Mirrors the auto-dream path the CLI fires after `said ask`.
     * @param {bigint} min_queries
     * @returns {boolean}
     */
    dream(min_queries) {
        const ret = wasm.saidbrain_dream(this.__wbg_ptr, min_queries);
        return ret !== 0;
    }
    /**
     * Drop ALL tombstones (no-keep). Use only after explicit user confirm.
     * @returns {number}
     */
    drop_history() {
        const ret = wasm.saidbrain_drop_history(this.__wbg_ptr);
        return ret >>> 0;
    }
    /**
     * Drop tombstones older than the N most recent per doc_id.
     * @param {number} keep_per_doc
     * @returns {number}
     */
    drop_history_keep(keep_per_doc) {
        const ret = wasm.saidbrain_drop_history_keep(this.__wbg_ptr, keep_per_doc);
        return ret >>> 0;
    }
    /**
     * Replace the content of an existing memory (creates a new version,
     * keeping the old one in the lineage). Returns the new frame id.
     * @param {string} doc_id
     * @param {string} content
     * @param {string | null} [title]
     * @returns {bigint}
     */
    edit(doc_id, content, title) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(content, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        var ptr2 = isLikeNone(title) ? 0 : passStringToWasm0(title, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        var len2 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_edit(this.__wbg_ptr, ptr0, len0, ptr1, len1, ptr2, len2);
        return BigInt.asUintN(64, ret);
    }
    /**
     * Filename the user dropped — used by save-as-download.
     * @returns {string}
     */
    get filename() {
        let deferred1_0;
        let deferred1_1;
        try {
            const ret = wasm.saidbrain_filename(this.__wbg_ptr);
            deferred1_0 = ret[0];
            deferred1_1 = ret[1];
            return getStringFromWasm0(ret[0], ret[1]);
        } finally {
            wasm.__wbindgen_free(deferred1_0, deferred1_1, 1);
        }
    }
    /**
     * Full content + metadata for a single memory.
     * @param {string} doc_id
     * @returns {any}
     */
    get_memory(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_get_memory(this.__wbg_ptr, ptr0, len0);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * History tab — append-only audit log, newest first.
     * @param {number} limit
     * @returns {any}
     */
    history(limit) {
        const ret = wasm.saidbrain_history(this.__wbg_ptr, limit);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Bulk-import: each (doc_id, content) pair becomes a memory.
     * Mirrors a subset of CLI `said import` (text/JSON path only).
     * @param {any[]} doc_ids
     * @param {any[]} contents
     * @param {any[]} titles
     * @returns {number}
     */
    import_text(doc_ids, contents, titles) {
        const ptr0 = passArrayJsValueToWasm0(doc_ids, wasm.__wbindgen_malloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passArrayJsValueToWasm0(contents, wasm.__wbindgen_malloc);
        const len1 = WASM_VECTOR_LEN;
        const ptr2 = passArrayJsValueToWasm0(titles, wasm.__wbindgen_malloc);
        const len2 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_import_text(this.__wbg_ptr, ptr0, len0, ptr1, len1, ptr2, len2);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return ret[0] >>> 0;
    }
    /**
     * Stream 1: extract a DOCX's text and index it as searchable memories.
     * Returns the number of paragraph segments indexed. Available to all brains.
     * @param {Uint8Array} bytes
     * @param {string} filename
     * @returns {number}
     */
    ingest_document_search(bytes, filename) {
        const ptr0 = passArray8ToWasm0(bytes, wasm.__wbindgen_malloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(filename, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_ingest_document_search(this.__wbg_ptr, ptr0, len0, ptr1, len1);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return ret[0] >>> 0;
    }
    /**
     * Add a doc_id to legal hold under a case_id. Returns count of frames
     * now under hold.
     * @param {string} doc_id
     * @param {string} case_id
     * @returns {number}
     */
    legal_hold_add(doc_id, case_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(case_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_legal_hold_add(this.__wbg_ptr, ptr0, len0, ptr1, len1);
        return ret >>> 0;
    }
    /**
     * Release a doc_id from legal hold for a case_id.
     * @param {string} doc_id
     * @param {string} case_id
     * @returns {number}
     */
    legal_hold_release(doc_id, case_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(case_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_legal_hold_release(this.__wbg_ptr, ptr0, len0, ptr1, len1);
        return ret >>> 0;
    }
    /**
     * Lineage of a doc_id — chronological list of all versions.
     * @param {string} doc_id
     * @returns {any}
     */
    lineage(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_lineage(this.__wbg_ptr, ptr0, len0);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * List active memories with previews. `offset`/`limit` paginate.
     * @param {number} offset
     * @param {number} limit
     * @returns {any}
     */
    list_memories(offset, limit) {
        const ret = wasm.saidbrain_list_memories(this.__wbg_ptr, offset, limit);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Brain mode (Portable / Enterprise). Read-only — set at create time.
     * @returns {string}
     */
    get mode() {
        let deferred1_0;
        let deferred1_1;
        try {
            const ret = wasm.saidbrain_mode(this.__wbg_ptr);
            deferred1_0 = ret[0];
            deferred1_1 = ret[1];
            return getStringFromWasm0(ret[0], ret[1]);
        } finally {
            wasm.__wbindgen_free(deferred1_0, deferred1_1, 1);
        }
    }
    /**
     * Load a `.said` file from in-memory bytes.
     *
     * `bytes` — the raw file contents from the browser's File API
     * `filename` — display name + default for the save-as-download flow
     * `encoder_*` — the three blobs (tokenizer.json, model.safetensors,
     *   config.json). Pass empty `Uint8Array`s to skip the encoder load
     *   (search degrades to lexical-only).
     * @param {Uint8Array} bytes
     * @param {string} filename
     * @param {Uint8Array} encoder_tokenizer
     * @param {Uint8Array} encoder_safetensors
     * @param {Uint8Array} encoder_config
     */
    constructor(bytes, filename, encoder_tokenizer, encoder_safetensors, encoder_config) {
        const ptr0 = passArray8ToWasm0(bytes, wasm.__wbindgen_malloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(filename, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ptr2 = passArray8ToWasm0(encoder_tokenizer, wasm.__wbindgen_malloc);
        const len2 = WASM_VECTOR_LEN;
        const ptr3 = passArray8ToWasm0(encoder_safetensors, wasm.__wbindgen_malloc);
        const len3 = WASM_VECTOR_LEN;
        const ptr4 = passArray8ToWasm0(encoder_config, wasm.__wbindgen_malloc);
        const len4 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_new(ptr0, len0, ptr1, len1, ptr2, len2, ptr3, len3, ptr4, len4);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        this.__wbg_ptr = ret[0] >>> 0;
        SaidBrainFinalization.register(this, this.__wbg_ptr, this);
        return this;
    }
    /**
     * Rebuild the search index after edits. Run before `save_to_bytes`.
     */
    rebuild_index() {
        const ret = wasm.saidbrain_rebuild_index(this.__wbg_ptr);
        if (ret[1]) {
            throw takeFromExternrefTable0(ret[0]);
        }
    }
    /**
     * Add a new memory.
     * @param {string} content
     * @param {string | null} [title]
     * @returns {bigint}
     */
    remember(content, title) {
        const ptr0 = passStringToWasm0(content, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        var ptr1 = isLikeNone(title) ? 0 : passStringToWasm0(title, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        var len1 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_remember(this.__wbg_ptr, ptr0, len0, ptr1, len1);
        return BigInt.asUintN(64, ret);
    }
    /**
     * Generic pillar-aware remember. Useful for distillation flows
     * (the LLM decides "this is a fact, write to Semantic").
     * @param {string} content
     * @param {string} pillar
     * @param {string | null | undefined} doc_id
     * @param {string | null | undefined} title
     * @param {any[]} tags
     * @returns {bigint}
     */
    remember_with_pillar(content, pillar, doc_id, title, tags) {
        const ptr0 = passStringToWasm0(content, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(pillar, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        var ptr2 = isLikeNone(doc_id) ? 0 : passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        var len2 = WASM_VECTOR_LEN;
        var ptr3 = isLikeNone(title) ? 0 : passStringToWasm0(title, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        var len3 = WASM_VECTOR_LEN;
        const ptr4 = passArrayJsValueToWasm0(tags, wasm.__wbindgen_malloc);
        const len4 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_remember_with_pillar(this.__wbg_ptr, ptr0, len0, ptr1, len1, ptr2, len2, ptr3, len3, ptr4, len4);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return BigInt.asUintN(64, ret[0]);
    }
    /**
     * Restore a tombstoned memory.
     * @param {string} doc_id
     */
    restore(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_restore(this.__wbg_ptr, ptr0, len0);
        if (ret[1]) {
            throw takeFromExternrefTable0(ret[0]);
        }
    }
    /**
     * Salience score — pure scorer (no mutation). Same as MCP `salience`.
     * Returns score (0-100), band (low|medium|high), and tag list. Used
     * before `remember` to decide whether content is worth keeping.
     * @param {string} text
     * @param {string | null} [pillar]
     * @returns {any}
     */
    salience_score(text, pillar) {
        const ptr0 = passStringToWasm0(text, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        var ptr1 = isLikeNone(pillar) ? 0 : passStringToWasm0(pillar, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        var len1 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_salience_score(this.__wbg_ptr, ptr0, len0, ptr1, len1);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Serialize the (possibly edited) brain back to a Vec<u8>. The JS
     * side wraps this in a Blob and triggers a download.
     * @returns {Uint8Array}
     */
    save_to_bytes() {
        const ret = wasm.saidbrain_save_to_bytes(this.__wbg_ptr);
        if (ret[3]) {
            throw takeFromExternrefTable0(ret[2]);
        }
        var v1 = getArrayU8FromWasm0(ret[0], ret[1]).slice();
        wasm.__wbindgen_free(ret[0], ret[1] * 1, 1);
        return v1;
    }
    /**
     * Lexical grep — fast keyword search with snippets.
     * @param {string} query
     * @param {number} k
     * @returns {any}
     */
    search(query, k) {
        const ptr0 = passStringToWasm0(query, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_search(this.__wbg_ptr, ptr0, len0, k);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * List Stream-1 (search) source documents as [{filename, segments}].
     * Available to ALL brains — this is the visible record that a search
     * ingest landed, independent of the Enterprise-only vault.
     * @returns {any}
     */
    search_document_list() {
        const ret = wasm.saidbrain_search_document_list(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Remove a search-only (Stream 1) document from the index by filename.
     * Deletes every `{base}::para_N` frame for that document. Available to ALL
     * brains (search is not enterprise-only). Returns the number of segments
     * removed. Does NOT touch the vault — a doc also stored in the vault keeps
     * its vault copy (erase it from the Vault section for that).
     * @param {string} filename
     * @returns {number}
     */
    search_document_remove(filename) {
        const ptr0 = passStringToWasm0(filename, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_search_document_remove(this.__wbg_ptr, ptr0, len0);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return ret[0] >>> 0;
    }
    /**
     * Write an Episodic frame summarising one full conversation turn.
     * Mirrors the MCP `session_end` tool: tagged `event:session_end`
     * + optional `session:<id>` + caller tags. Default doc_id is
     * `ep_session_<id>` when session_id is provided, or auto `mem_N`.
     * Returns the new frame_id.
     * @param {string} summary
     * @param {string | null | undefined} session_id
     * @param {any[]} tags
     * @returns {bigint}
     */
    session_end(summary, session_id, tags) {
        const ptr0 = passStringToWasm0(summary, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        var ptr1 = isLikeNone(session_id) ? 0 : passStringToWasm0(session_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        var len1 = WASM_VECTOR_LEN;
        const ptr2 = passArrayJsValueToWasm0(tags, wasm.__wbindgen_malloc);
        const len2 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_session_end(this.__wbg_ptr, ptr0, len0, ptr1, len1, ptr2, len2);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return BigInt.asUintN(64, ret[0]);
    }
    /**
     * Distinct sources currently present (parent file paths or collection IDs).
     * @returns {any}
     */
    sources() {
        const ret = wasm.saidbrain_sources(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Stats panel — single call returns everything the dashboard needs.
     * @returns {any}
     */
    stats() {
        const ret = wasm.saidbrain_stats(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Symbol search — exact-name lookup against the symbol index.
     * @param {string} name
     * @param {number} max
     * @returns {any}
     */
    sym(name, max) {
        const ptr0 = passStringToWasm0(name, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_sym(this.__wbg_ptr, ptr0, len0, max);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Symbol prefix list — used for "find by partial name".
     * @param {string} prefix
     * @param {number} max
     * @returns {any}
     */
    sym_list(prefix, max) {
        const ptr0 = passStringToWasm0(prefix, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_sym_list(this.__wbg_ptr, ptr0, len0, max);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Total number of symbols indexed across all active frames.
     * @returns {number}
     */
    symbol_count() {
        const ret = wasm.saidbrain_symbol_count(this.__wbg_ptr);
        return ret >>> 0;
    }
    /**
     * Tombstones tab — Recycle Bin view of soft-deleted memories.
     * @returns {any}
     */
    tombstones() {
        const ret = wasm.saidbrain_tombstones(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Write an Episodic frame for a single tool invocation. Mirrors the
     * MCP `tool_completion` tool. Recurring tool+args+result patterns
     * distill to Procedural at the next dream cycle.
     * @param {string} tool_name
     * @param {string} body
     * @param {any[]} tags
     * @returns {bigint}
     */
    tool_completion(tool_name, body, tags) {
        const ptr0 = passStringToWasm0(tool_name, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(body, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ptr2 = passArrayJsValueToWasm0(tags, wasm.__wbindgen_malloc);
        const len2 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_tool_completion(this.__wbg_ptr, ptr0, len0, ptr1, len1, ptr2, len2);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return BigInt.asUintN(64, ret[0]);
    }
    /**
     * Read the vault audit log as [{seq, action, doc_id, target, actor, at,
     * prev_hash, note}], oldest first. ENTERPRISE.
     * @returns {any}
     */
    vault_audit() {
        const ret = wasm.saidbrain_vault_audit(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Append a vault audit event from the UI (e.g. a "view" action that has no
     * other binding). ENTERPRISE.
     * @param {string} action
     * @param {string} doc_id
     * @param {string} note
     */
    vault_audit_log(action, doc_id, note) {
        const ptr0 = passStringToWasm0(action, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ptr2 = passStringToWasm0(note, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len2 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_audit_log(this.__wbg_ptr, ptr0, len0, ptr1, len1, ptr2, len2);
        if (ret[1]) {
            throw takeFromExternrefTable0(ret[0]);
        }
    }
    /**
     * Verify the audit hash-chain integrity. Returns
     * `{ok, count, broken_at, detail}`. ENTERPRISE.
     * @returns {any}
     */
    vault_audit_verify() {
        const ret = wasm.saidbrain_vault_audit_verify(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Whether a document may be deleted right now: `{ok, reason}`. ENTERPRISE.
     * @param {string} doc_id
     * @returns {any}
     */
    vault_deletable(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_deletable(this.__wbg_ptr, ptr0, len0);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Erase a vault document (GDPR Art. 17). Refused if under legal hold or
     * before its disposition date. Removes the manifest, retention record,
     * byte-exact tombstone, and any dedup assets not shared with other docs;
     * records a `delete` audit event. Returns a tamper-evident deletion
     * certificate `{doc_id, filename, original_blake3, deleted_by, deleted_at,
     * reason, assets_removed, tombstone_removed, seal}`. ENTERPRISE.
     * @param {string} doc_id
     * @param {string} reason
     * @returns {any}
     */
    vault_delete(doc_id, reason) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(reason, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_delete(this.__wbg_ptr, ptr0, len0, ptr1, len1);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Stream 2 (ENTERPRISE): store a DOCX in the vault. legal=true keeps the
     * byte-exact tombstone; false is slim. Returns the doc_id.
     * @param {Uint8Array} bytes
     * @param {string} filename
     * @param {boolean} legal
     * @returns {string}
     */
    vault_ingest(bytes, filename, legal) {
        let deferred4_0;
        let deferred4_1;
        try {
            const ptr0 = passArray8ToWasm0(bytes, wasm.__wbindgen_malloc);
            const len0 = WASM_VECTOR_LEN;
            const ptr1 = passStringToWasm0(filename, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
            const len1 = WASM_VECTOR_LEN;
            const ret = wasm.saidbrain_vault_ingest(this.__wbg_ptr, ptr0, len0, ptr1, len1, legal);
            var ptr3 = ret[0];
            var len3 = ret[1];
            if (ret[3]) {
                ptr3 = 0; len3 = 0;
                throw takeFromExternrefTable0(ret[2]);
            }
            deferred4_0 = ptr3;
            deferred4_1 = len3;
            return getStringFromWasm0(ptr3, len3);
        } finally {
            wasm.__wbindgen_free(deferred4_0, deferred4_1, 1);
        }
    }
    /**
     * List vault documents with full manifest provenance. ENTERPRISE.
     *
     * Returns one row per document with everything the Manifest holds:
     * `{doc_id, filename, tier, size_bytes, format, ingested_at, ingested_by,
     *   tags, part_count, blake3}`. The UI uses this for sortable columns,
     * tag facets, the detail drawer, and the integrity surface.
     * @returns {any}
     */
    vault_list() {
        const ret = wasm.saidbrain_vault_list(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Rebuild a document from dedup parts (structural). Returns the bytes. ENTERPRISE.
     * @param {string} doc_id
     * @returns {Uint8Array}
     */
    vault_rebuild(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_rebuild(this.__wbg_ptr, ptr0, len0);
        if (ret[3]) {
            throw takeFromExternrefTable0(ret[2]);
        }
        var v2 = getArrayU8FromWasm0(ret[0], ret[1]).slice();
        wasm.__wbindgen_free(ret[0], ret[1] * 1, 1);
        return v2;
    }
    /**
     * Restore the byte-exact original (legal only). Returns the bytes. ENTERPRISE.
     * @param {string} doc_id
     * @returns {Uint8Array}
     */
    vault_restore(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_restore(this.__wbg_ptr, ptr0, len0);
        if (ret[3]) {
            throw takeFromExternrefTable0(ret[2]);
        }
        var v2 = getArrayU8FromWasm0(ret[0], ret[1]).slice();
        wasm.__wbindgen_free(ret[0], ret[1] * 1, 1);
        return v2;
    }
    /**
     * Read a document's retention record as
     * `{doc_id, class, disposition_at, legal_hold, reason, updated_at}`, or
     * null if none is set. ENTERPRISE.
     * @param {string} doc_id
     * @returns {any}
     */
    vault_retention_get(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_retention_get(this.__wbg_ptr, ptr0, len0);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
    /**
     * Place or release a legal hold. A held document cannot be deleted or
     * compacted. ENTERPRISE.
     * @param {string} doc_id
     * @param {boolean} hold
     * @param {string} reason
     */
    vault_set_hold(doc_id, hold, reason) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(reason, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_set_hold(this.__wbg_ptr, ptr0, len0, hold, ptr1, len1);
        if (ret[1]) {
            throw takeFromExternrefTable0(ret[0]);
        }
    }
    /**
     * Set a document's retention class + disposition date (unix seconds, 0 =
     * none). Preserves any existing legal hold. ENTERPRISE.
     * @param {string} doc_id
     * @param {string} _class
     * @param {number} disposition_at
     * @param {string} reason
     */
    vault_set_retention(doc_id, _class, disposition_at, reason) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(_class, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ptr2 = passStringToWasm0(reason, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len2 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_set_retention(this.__wbg_ptr, ptr0, len0, ptr1, len1, disposition_at, ptr2, len2);
        if (ret[1]) {
            throw takeFromExternrefTable0(ret[0]);
        }
    }
    /**
     * Vault-wide dedup + storage savings. ENTERPRISE.
     *
     * Walks every manifest's zip-entry asset hashes: `total_parts` counts all
     * part references across documents; `unique_parts` counts distinct asset
     * hashes actually stored. The difference is what content-addressed dedup
     * saved. `raw_bytes` is the sum of original document sizes. Returns
     * `{doc_count, total_parts, unique_parts, shared_parts, raw_bytes,
     *   legal_count, slim_count}`.
     * @returns {any}
     */
    vault_stats() {
        const ret = wasm.saidbrain_vault_stats(this.__wbg_ptr);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
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
     * @param {string} doc_id
     * @returns {any}
     */
    vault_verify(doc_id) {
        const ptr0 = passStringToWasm0(doc_id, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ret = wasm.saidbrain_vault_verify(this.__wbg_ptr, ptr0, len0);
        if (ret[2]) {
            throw takeFromExternrefTable0(ret[1]);
        }
        return takeFromExternrefTable0(ret[0]);
    }
}
if (Symbol.dispose) SaidBrain.prototype[Symbol.dispose] = SaidBrain.prototype.free;

export function _start() {
    wasm._start();
}

/**
 * Return the list of available agent prompts as JSON. Mirrors the MCP
 * `prompts/list` response shape so a thin JS wrapper can present them
 * in a UI menu identically to MCP clients (Claude Desktop, Cursor).
 * @returns {any}
 */
export function list_prompts() {
    const ret = wasm.list_prompts();
    if (ret[2]) {
        throw takeFromExternrefTable0(ret[1]);
    }
    return takeFromExternrefTable0(ret[0]);
}

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
 * @param {string} role
 * @param {string} files_json
 * @param {number} total_docs
 * @returns {string}
 */
export function system_prompt(role, files_json, total_docs) {
    let deferred3_0;
    let deferred3_1;
    try {
        const ptr0 = passStringToWasm0(role, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len0 = WASM_VECTOR_LEN;
        const ptr1 = passStringToWasm0(files_json, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
        const len1 = WASM_VECTOR_LEN;
        const ret = wasm.system_prompt(ptr0, len0, ptr1, len1, total_docs);
        deferred3_0 = ret[0];
        deferred3_1 = ret[1];
        return getStringFromWasm0(ret[0], ret[1]);
    } finally {
        wasm.__wbindgen_free(deferred3_0, deferred3_1, 1);
    }
}

function __wbg_get_imports() {
    const import0 = {
        __proto__: null,
        __wbg_Error_83742b46f01ce22d: function(arg0, arg1) {
            const ret = Error(getStringFromWasm0(arg0, arg1));
            return ret;
        },
        __wbg_String_8564e559799eccda: function(arg0, arg1) {
            const ret = String(arg1);
            const ptr1 = passStringToWasm0(ret, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
            const len1 = WASM_VECTOR_LEN;
            getDataViewMemory0().setInt32(arg0 + 4 * 1, len1, true);
            getDataViewMemory0().setInt32(arg0 + 4 * 0, ptr1, true);
        },
        __wbg___wbindgen_is_string_7ef6b97b02428fae: function(arg0) {
            const ret = typeof(arg0) === 'string';
            return ret;
        },
        __wbg___wbindgen_string_get_395e606bd0ee4427: function(arg0, arg1) {
            const obj = arg1;
            const ret = typeof(obj) === 'string' ? obj : undefined;
            var ptr1 = isLikeNone(ret) ? 0 : passStringToWasm0(ret, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
            var len1 = WASM_VECTOR_LEN;
            getDataViewMemory0().setInt32(arg0 + 4 * 1, len1, true);
            getDataViewMemory0().setInt32(arg0 + 4 * 0, ptr1, true);
        },
        __wbg___wbindgen_throw_6ddd609b62940d55: function(arg0, arg1) {
            throw new Error(getStringFromWasm0(arg0, arg1));
        },
        __wbg_error_a6fa202b58aa1cd3: function(arg0, arg1) {
            let deferred0_0;
            let deferred0_1;
            try {
                deferred0_0 = arg0;
                deferred0_1 = arg1;
                console.error(getStringFromWasm0(arg0, arg1));
            } finally {
                wasm.__wbindgen_free(deferred0_0, deferred0_1, 1);
            }
        },
        __wbg_getRandomValues_3f44b700395062e5: function() { return handleError(function (arg0, arg1) {
            globalThis.crypto.getRandomValues(getArrayU8FromWasm0(arg0, arg1));
        }, arguments); },
        __wbg_new_227d7c05414eb861: function() {
            const ret = new Error();
            return ret;
        },
        __wbg_new_49d5571bd3f0c4d4: function() {
            const ret = new Map();
            return ret;
        },
        __wbg_new_a70fbab9066b301f: function() {
            const ret = new Array();
            return ret;
        },
        __wbg_new_ab79df5bd7c26067: function() {
            const ret = new Object();
            return ret;
        },
        __wbg_now_16f0c993d5dd6c27: function() {
            const ret = Date.now();
            return ret;
        },
        __wbg_set_282384002438957f: function(arg0, arg1, arg2) {
            arg0[arg1 >>> 0] = arg2;
        },
        __wbg_set_6be42768c690e380: function(arg0, arg1, arg2) {
            arg0[arg1] = arg2;
        },
        __wbg_set_bf7251625df30a02: function(arg0, arg1, arg2) {
            const ret = arg0.set(arg1, arg2);
            return ret;
        },
        __wbg_stack_3b0d974bbf31e44f: function(arg0, arg1) {
            const ret = arg1.stack;
            const ptr1 = passStringToWasm0(ret, wasm.__wbindgen_malloc, wasm.__wbindgen_realloc);
            const len1 = WASM_VECTOR_LEN;
            getDataViewMemory0().setInt32(arg0 + 4 * 1, len1, true);
            getDataViewMemory0().setInt32(arg0 + 4 * 0, ptr1, true);
        },
        __wbindgen_cast_0000000000000001: function(arg0) {
            // Cast intrinsic for `F64 -> Externref`.
            const ret = arg0;
            return ret;
        },
        __wbindgen_cast_0000000000000002: function(arg0, arg1) {
            // Cast intrinsic for `Ref(String) -> Externref`.
            const ret = getStringFromWasm0(arg0, arg1);
            return ret;
        },
        __wbindgen_cast_0000000000000003: function(arg0) {
            // Cast intrinsic for `U64 -> Externref`.
            const ret = BigInt.asUintN(64, arg0);
            return ret;
        },
        __wbindgen_init_externref_table: function() {
            const table = wasm.__wbindgen_externrefs;
            const offset = table.grow(4);
            table.set(0, undefined);
            table.set(offset + 0, undefined);
            table.set(offset + 1, null);
            table.set(offset + 2, true);
            table.set(offset + 3, false);
        },
    };
    return {
        __proto__: null,
        "./said_wasm_bg.js": import0,
    };
}

const SaidBrainFinalization = (typeof FinalizationRegistry === 'undefined')
    ? { register: () => {}, unregister: () => {} }
    : new FinalizationRegistry(ptr => wasm.__wbg_saidbrain_free(ptr >>> 0, 1));

function addToExternrefTable0(obj) {
    const idx = wasm.__externref_table_alloc();
    wasm.__wbindgen_externrefs.set(idx, obj);
    return idx;
}

function getArrayU8FromWasm0(ptr, len) {
    ptr = ptr >>> 0;
    return getUint8ArrayMemory0().subarray(ptr / 1, ptr / 1 + len);
}

let cachedDataViewMemory0 = null;
function getDataViewMemory0() {
    if (cachedDataViewMemory0 === null || cachedDataViewMemory0.buffer.detached === true || (cachedDataViewMemory0.buffer.detached === undefined && cachedDataViewMemory0.buffer !== wasm.memory.buffer)) {
        cachedDataViewMemory0 = new DataView(wasm.memory.buffer);
    }
    return cachedDataViewMemory0;
}

function getStringFromWasm0(ptr, len) {
    ptr = ptr >>> 0;
    return decodeText(ptr, len);
}

let cachedUint8ArrayMemory0 = null;
function getUint8ArrayMemory0() {
    if (cachedUint8ArrayMemory0 === null || cachedUint8ArrayMemory0.byteLength === 0) {
        cachedUint8ArrayMemory0 = new Uint8Array(wasm.memory.buffer);
    }
    return cachedUint8ArrayMemory0;
}

function handleError(f, args) {
    try {
        return f.apply(this, args);
    } catch (e) {
        const idx = addToExternrefTable0(e);
        wasm.__wbindgen_exn_store(idx);
    }
}

function isLikeNone(x) {
    return x === undefined || x === null;
}

function passArray8ToWasm0(arg, malloc) {
    const ptr = malloc(arg.length * 1, 1) >>> 0;
    getUint8ArrayMemory0().set(arg, ptr / 1);
    WASM_VECTOR_LEN = arg.length;
    return ptr;
}

function passArrayJsValueToWasm0(array, malloc) {
    const ptr = malloc(array.length * 4, 4) >>> 0;
    for (let i = 0; i < array.length; i++) {
        const add = addToExternrefTable0(array[i]);
        getDataViewMemory0().setUint32(ptr + 4 * i, add, true);
    }
    WASM_VECTOR_LEN = array.length;
    return ptr;
}

function passStringToWasm0(arg, malloc, realloc) {
    if (realloc === undefined) {
        const buf = cachedTextEncoder.encode(arg);
        const ptr = malloc(buf.length, 1) >>> 0;
        getUint8ArrayMemory0().subarray(ptr, ptr + buf.length).set(buf);
        WASM_VECTOR_LEN = buf.length;
        return ptr;
    }

    let len = arg.length;
    let ptr = malloc(len, 1) >>> 0;

    const mem = getUint8ArrayMemory0();

    let offset = 0;

    for (; offset < len; offset++) {
        const code = arg.charCodeAt(offset);
        if (code > 0x7F) break;
        mem[ptr + offset] = code;
    }
    if (offset !== len) {
        if (offset !== 0) {
            arg = arg.slice(offset);
        }
        ptr = realloc(ptr, len, len = offset + arg.length * 3, 1) >>> 0;
        const view = getUint8ArrayMemory0().subarray(ptr + offset, ptr + len);
        const ret = cachedTextEncoder.encodeInto(arg, view);

        offset += ret.written;
        ptr = realloc(ptr, len, offset, 1) >>> 0;
    }

    WASM_VECTOR_LEN = offset;
    return ptr;
}

function takeFromExternrefTable0(idx) {
    const value = wasm.__wbindgen_externrefs.get(idx);
    wasm.__externref_table_dealloc(idx);
    return value;
}

let cachedTextDecoder = new TextDecoder('utf-8', { ignoreBOM: true, fatal: true });
cachedTextDecoder.decode();
const MAX_SAFARI_DECODE_BYTES = 2146435072;
let numBytesDecoded = 0;
function decodeText(ptr, len) {
    numBytesDecoded += len;
    if (numBytesDecoded >= MAX_SAFARI_DECODE_BYTES) {
        cachedTextDecoder = new TextDecoder('utf-8', { ignoreBOM: true, fatal: true });
        cachedTextDecoder.decode();
        numBytesDecoded = len;
    }
    return cachedTextDecoder.decode(getUint8ArrayMemory0().subarray(ptr, ptr + len));
}

const cachedTextEncoder = new TextEncoder();

if (!('encodeInto' in cachedTextEncoder)) {
    cachedTextEncoder.encodeInto = function (arg, view) {
        const buf = cachedTextEncoder.encode(arg);
        view.set(buf);
        return {
            read: arg.length,
            written: buf.length
        };
    };
}

let WASM_VECTOR_LEN = 0;

let wasmModule, wasm;
function __wbg_finalize_init(instance, module) {
    wasm = instance.exports;
    wasmModule = module;
    cachedDataViewMemory0 = null;
    cachedUint8ArrayMemory0 = null;
    wasm.__wbindgen_start();
    return wasm;
}

async function __wbg_load(module, imports) {
    if (typeof Response === 'function' && module instanceof Response) {
        if (typeof WebAssembly.instantiateStreaming === 'function') {
            try {
                return await WebAssembly.instantiateStreaming(module, imports);
            } catch (e) {
                const validResponse = module.ok && expectedResponseType(module.type);

                if (validResponse && module.headers.get('Content-Type') !== 'application/wasm') {
                    console.warn("`WebAssembly.instantiateStreaming` failed because your server does not serve Wasm with `application/wasm` MIME type. Falling back to `WebAssembly.instantiate` which is slower. Original error:\n", e);

                } else { throw e; }
            }
        }

        const bytes = await module.arrayBuffer();
        return await WebAssembly.instantiate(bytes, imports);
    } else {
        const instance = await WebAssembly.instantiate(module, imports);

        if (instance instanceof WebAssembly.Instance) {
            return { instance, module };
        } else {
            return instance;
        }
    }

    function expectedResponseType(type) {
        switch (type) {
            case 'basic': case 'cors': case 'default': return true;
        }
        return false;
    }
}

function initSync(module) {
    if (wasm !== undefined) return wasm;


    if (module !== undefined) {
        if (Object.getPrototypeOf(module) === Object.prototype) {
            ({module} = module)
        } else {
            console.warn('using deprecated parameters for `initSync()`; pass a single object instead')
        }
    }

    const imports = __wbg_get_imports();
    if (!(module instanceof WebAssembly.Module)) {
        module = new WebAssembly.Module(module);
    }
    const instance = new WebAssembly.Instance(module, imports);
    return __wbg_finalize_init(instance, module);
}

async function __wbg_init(module_or_path) {
    if (wasm !== undefined) return wasm;


    if (module_or_path !== undefined) {
        if (Object.getPrototypeOf(module_or_path) === Object.prototype) {
            ({module_or_path} = module_or_path)
        } else {
            console.warn('using deprecated parameters for the initialization function; pass a single object instead')
        }
    }

    if (module_or_path === undefined) {
        module_or_path = new URL('said_wasm_bg.wasm', import.meta.url);
    }
    const imports = __wbg_get_imports();

    if (typeof module_or_path === 'string' || (typeof Request === 'function' && module_or_path instanceof Request) || (typeof URL === 'function' && module_or_path instanceof URL)) {
        module_or_path = fetch(module_or_path);
    }

    const { instance, module } = await __wbg_load(await module_or_path, imports);

    return __wbg_finalize_init(instance, module);
}

export { initSync, __wbg_init as default };
