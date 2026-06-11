# Research Backlog

Things I should understand more deeply about this codebase before I touch them. Each entry is a
`### [ ]` checkbox with a Goal explaining what to study and why. I add to this whenever a session
reveals a gap; I check items off when I've learned enough to act safely.

### [ ] The gate decision pipeline end to end
**Goal:** Trace a package from intake → resolve → vuln sources → policy controls → decision →
promotion bridge. Know exactly where a change could alter a security outcome, so I never weaken one
by accident.

### [ ] The signed policy + audit hash-chain
**Goal:** Understand how FirewallPolicy is versioned, signed, and how the audit ledger chains. Any
change near here must preserve the integrity guarantees.

### [ ] Test coverage map
**Goal:** Identify which controls have real tests and which don't. Run mutation testing to find
tests that pass but don't actually catch bugs. Prioritise closing those gaps.
