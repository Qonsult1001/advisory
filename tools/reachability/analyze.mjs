#!/usr/bin/env node
/**
 * npm reachability analyzer (contextual analysis PoC).
 *
 * Input  (argv[2]): path to the consuming project to analyze.
 * Input  (stdin)  : JSON { targets: [{ id, package, symbols: [..] }] }
 *                   - package: the vulnerable npm package name
 *                   - symbols: vulnerable exported function/member names (optional; from OSV/GHSA)
 * Output (stdout) : JSON { results: [{ id, reachability, detail }] }
 *
 * Method (honest about its limits):
 *   1. Walk the project's own source files (skip node_modules) and parse each with acorn.
 *   2. Record, per file, which packages are imported (import/require) and under what local binding,
 *      then which of those bindings are actually *called* or member-accessed.
 *   3. Reachability per target:
 *        NotReachable  - the vulnerable package is never imported by first-party code.
 *        Reachable     - the package is imported AND (no symbols given => any use) OR
 *                        (symbols given => one of those symbols is accessed/called on the binding).
 *        Unknown       - imported but we can't confirm the specific symbol is used (dynamic access,
 *                        re-export, namespace spread), or parsing was incomplete.
 *
 * This is a single-hop, first-party-call analysis — NOT a full transitive call graph through
 * dependency internals. It removes the largest class of false positives ("CVE in a package you
 * never call") while being explicit (Unknown) where it cannot prove reachability.
 */
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, extname } from 'node:path';
import * as acorn from 'acorn';

const SRC_EXT = new Set(['.js', '.mjs', '.cjs', '.jsx', '.ts', '.tsx']);

function readStdin() {
  try { return readFileSync(0, 'utf8'); } catch { return ''; }
}

function listSourceFiles(root) {
  const out = [];
  const walk = (dir) => {
    let entries;
    try { entries = readdirSync(dir); } catch { return; }
    for (const name of entries) {
      if (name === 'node_modules' || name === '.git' || name === 'dist' || name === 'build') continue;
      const full = join(dir, name);
      let st;
      try { st = statSync(full); } catch { continue; }
      if (st.isDirectory()) walk(full);
      else if (SRC_EXT.has(extname(name))) out.push(full);
    }
  };
  walk(root);
  return out;
}

// Very small source scan: collect (package -> local binding names) and (binding -> accessed members).
function analyzeFile(code) {
  const imports = new Map();   // package name -> Set(local binding)
  const usedBindings = new Set();        // bindings that are referenced at all
  const memberAccess = new Map();        // binding -> Set(member names accessed)

  let ast;
  try {
    ast = acorn.parse(code, { ecmaVersion: 'latest', sourceType: 'module', allowReturnOutsideFunction: true });
  } catch {
    // Fall back to a regex sweep for require()/import when the parser chokes (e.g. raw TS types).
    const reqRe = /require\(\s*['"]([^'"]+)['"]\s*\)/g;
    const impRe = /from\s+['"]([^'"]+)['"]/g;
    let m;
    while ((m = reqRe.exec(code))) addImport(imports, m[1], '*');
    while ((m = impRe.exec(code))) addImport(imports, m[1], '*');
    return { imports, usedBindings, memberAccess, parsed: false };
  }

  const bindingToPkg = new Map();
  const visit = (node) => {
    if (!node || typeof node.type !== 'string') return;
    switch (node.type) {
      case 'ImportDeclaration': {
        const pkg = node.source.value;
        for (const spec of node.specifiers) {
          const local = spec.local.name;
          addImport(imports, pkg, local);
          bindingToPkg.set(local, pkg);
        }
        break;
      }
      case 'VariableDeclarator': {
        // const x = require('pkg')
        if (node.init && node.init.type === 'CallExpression' &&
            node.init.callee.name === 'require' && node.init.arguments[0]?.type === 'Literal') {
          const pkg = node.init.arguments[0].value;
          if (node.id.type === 'Identifier') { addImport(imports, pkg, node.id.name); bindingToPkg.set(node.id.name, pkg); }
          else addImport(imports, pkg, '*'); // destructured require
        }
        break;
      }
      case 'Identifier':
        if (bindingToPkg.has(node.name)) usedBindings.add(node.name);
        break;
      case 'MemberExpression':
        if (node.object?.type === 'Identifier' && bindingToPkg.has(node.object.name)) {
          usedBindings.add(node.object.name);
          if (node.property?.name) {
            if (!memberAccess.has(node.object.name)) memberAccess.set(node.object.name, new Set());
            memberAccess.get(node.object.name).add(node.property.name);
          }
        }
        break;
    }
    for (const key of Object.keys(node)) {
      const child = node[key];
      if (Array.isArray(child)) child.forEach(visit);
      else if (child && typeof child.type === 'string') visit(child);
    }
  };
  visit(ast);
  return { imports, usedBindings, memberAccess, bindingToPkg, parsed: true };
}

function addImport(map, pkg, local) {
  // Normalize subpath imports (e.g. "lodash/merge" -> "lodash", "@scope/x/y" -> "@scope/x").
  let base = pkg.startsWith('@') ? pkg.split('/').slice(0, 2).join('/') : pkg.split('/')[0];
  if (!map.has(base)) map.set(base, new Set());
  map.get(base).add(local);
}

function main() {
  const projectPath = process.argv[2];
  const input = JSON.parse(readStdin() || '{"targets":[]}');
  const targets = input.targets || [];

  const files = projectPath ? listSourceFiles(projectPath) : [];
  // Aggregate analysis across all first-party files.
  const pkgBindings = new Map();   // pkg -> Set(binding)
  const pkgMembersUsed = new Map();// pkg -> Set(member)
  const pkgImported = new Set();
  let anyParsed = false;

  for (const f of files) {
    let a;
    try { a = analyzeFile(readFileSync(f, 'utf8')); } catch { continue; }
    anyParsed = anyParsed || a.parsed;
    for (const [pkg, locals] of a.imports) {
      pkgImported.add(pkg);
      if (!pkgBindings.has(pkg)) pkgBindings.set(pkg, new Set());
      for (const l of locals) pkgBindings.get(pkg).add(l);
    }
    if (a.bindingToPkg) {
      for (const [binding, members] of a.memberAccess) {
        const pkg = a.bindingToPkg.get(binding);
        if (!pkg) continue;
        if (!pkgMembersUsed.has(pkg)) pkgMembersUsed.set(pkg, new Set());
        for (const m of members) pkgMembersUsed.get(pkg).add(m);
      }
    }
  }

  const results = targets.map((t) => {
    const pkg = t.package;
    const symbols = (t.symbols || []).filter(Boolean);
    if (!projectPath || files.length === 0)
      return { id: t.id, reachability: 'Unknown', detail: 'no consuming project source provided' };
    if (!pkgImported.has(pkg))
      return { id: t.id, reachability: 'NotReachable', detail: `package '${pkg}' is never imported by first-party code (${files.length} files scanned)` };

    const bindings = [...(pkgBindings.get(pkg) || [])];
    const wildcard = bindings.includes('*'); // destructured/namespace import — can't pin members
    const membersUsed = pkgMembersUsed.get(pkg) || new Set();

    if (symbols.length === 0)
      return { id: t.id, reachability: 'Reachable', detail: `package '${pkg}' is imported and used (no specific vulnerable symbol listed)` };

    const hit = symbols.find((s) => membersUsed.has(s));
    if (hit)
      return { id: t.id, reachability: 'Reachable', detail: `vulnerable symbol '${hit}' of '${pkg}' is accessed in first-party code` };
    if (wildcard || membersUsed.size === 0)
      return { id: t.id, reachability: 'Unknown', detail: `'${pkg}' imported but member usage is dynamic/namespace — cannot confirm symbol [${symbols.join(', ')}]` };
    return { id: t.id, reachability: 'NotReachable', detail: `'${pkg}' imported but none of the vulnerable symbols [${symbols.join(', ')}] are accessed (used: ${[...membersUsed].slice(0, 6).join(', ')})` };
  });

  process.stdout.write(JSON.stringify({ results, filesScanned: files.length, parsed: anyParsed }));
}

main();
