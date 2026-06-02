# STEP 0 — resolveEmployee (+ PRE.A loadRoleCatalog)

**Kind**: PRE.A deterministic (role-catalog load) + STEP 0 LLM-with-mandatory-confirmation (employee resolution)
**Authority**: workflow contract `PRE_A` + `S0` + K17 (in `metric-selection.workflow-contract.projection.json`), role-catalog projection K1–K4
**Runs**: before PRE (loadMetricRegistry)
**Outputs**: `role_catalog` map (in memory) + `evaluation_context.employee` (object form) + `employee.employee_provenance` + `evaluation_context.employee_resolution_log`

STEP 0 is the resolve-then-canonicalize front door. It eliminates the "evaluation blocks because the role was spelled differently" failure class by funneling all role normalization through one step backed by an authoritative Role_Catalog.

## PRE.A — loadRoleCatalog (deterministic, inline, no LLM)

Runs first so STEP 0 can canonicalize.

1. Scan `EVALUATION_ROLES_DIR` (default `./role-catalog/`) for `*.role.json`.
2. Validate each against `role-catalog-entry.schema.json`. Build a map keyed by `role_id`.
3. Resolve inheritance for entries with non-null `parent_role`:
   - `industry`: child overrides (child wins if it declares one; else inherit parent's).
   - `responsibility_tags`: set-union with dedup (child ∪ parent).
   - chain depth cap 8.
4. Fail-soft on every error (never block the run):

| Error | Action |
|---|---|
| File JSON parse / schema fail | skip file + `open_question` + proceed |
| `parent_role` absent / cycle / depth > 8 | load entry without inheritance + `open_question` + proceed |
| Duplicate `role_id` across two files | keep lexicographically-first filename + `open_question` + proceed |

> Only a missing match for the **evaluatee's own** role degrades to a caveat (STEP 0 canonicalization miss), never a block. A broken catalog file for some other role is irrelevant to this run.

## STEP 0 — resolveEmployee (LLM-with-mandatory-confirmation)

### Resolution priority (three sources, fixed order)

```
employee_id valid?  (non-empty, no path separator)
  └─ no → block_or_escalate (cause = employee_id_invalid)
  └─ yes ▼
employees/<employee_id>.json exists?
  ├─ YES → load + validate (employee.schema.json)
  │        ├─ ok   → source=authoritative_file, reliability=high
  │        └─ fail → block_or_escalate (parse/schema fail; DO NOT fall through)
  └─ NO  → user supplied a 1..10000-char description?
           ├─ YES → LLM parse → draft {role_id, industry, job_responsibilities, scenarios}
           │        → DISPLAY draft → request confirm | correct | decline
           │            ├─ confirm                      → source=user_dialog, reliability=high
           │            ├─ correct (≤5 rounds)          → apply + re-display + re-ask
           │            └─ decline | 120s timeout | 5-round-exhaust → inferred_fallback
           └─ NO  → inferred_fallback
                     → LLM best-guess (each field a value or "unknown")
                     → source=inferred_fallback, reliability=low,
                       caveat=employee_inferred_no_authoritative_source
                     → open_question listing unknown fields + absent sources
```

Notes:
- **Authoritative-file failure is a block, not a fall-through.** A present-but-broken employee file means someone tried to give an authoritative answer and it is corrupt — guessing past it would be worse than stopping.
- **User-dialog requires explicit confirmation.** Never silently accept the LLM's parse of a spoken description. Display all four fields; only `confirm` proceeds.
- **Persist every round** to `evaluation_context.employee_resolution_log` (one entry per round: displayed draft, response type `confirm|correct|decline|timeout`, corrections, final confirmed draft).

### Provenance object (K17)

```jsonc
{
  "source":      "authoritative_file" | "user_dialog" | "inferred_fallback",   // required
  "reliability": "high" | "low",                                                // required
  "caveat":      "employee_inferred_no_authoritative_source"                    // required when reliability=low
                 // may also contain / append "role_id_no_catalog_entry" on canonicalization miss
}
```

### Role canonicalization (R6)

1. Trim the resolved free-form role string. Empty/whitespace → `block_or_escalate` (cause = `role_string_empty`).
2. Case-insensitive exact match against each entry's `role_id`, then its `aliases`, in catalog iteration order, first-match-wins.
3. **Match** → `employee.role.role_id = matched role_id`; copy `industry` + `responsibility_tags` from the entry into `employee.role`.
4. **Miss** → `employee.role.role_id =` the trimmed free-form string; append `role_id_no_catalog_entry` to `employee_provenance.caveat` (dedup against any existing value).

### Single-writer rule (R6.5)

STEP 0 is the **only** step permitted to write `employee.role.role_id`. If any later step's output would change it, that is an `unauthorized_role_id_mutation` violation: reject the write, preserve the prior value, surface the error. Self-checked by the agent the same way K9/K13 are.

### employee.role object shape (R7)

```jsonc
"role": {
  "role_id":             "customer-service-ecommerce",  // required
  "industry":            "ecommerce",                     // required (empty string allowed = unset)
  "responsibility_tags": ["customer_facing", "tool_use"], // required (empty array allowed)
  "level":               "employee"                        // optional
}
```

### Backward compatibility (R16.2/16.3)

- A **bare-string** `evaluation_context.employee.role` (legacy) → wrap into the object form: `role_id` = trimmed string, `industry` = "", `responsibility_tags` = [], append `role_id_no_catalog_entry` caveat.
- An `employee.role` **already in valid object form** → leave unchanged, append no caveat.
- Neither string nor valid object → `block_or_escalate` (cause = `employee_role_invalid_form`).
- Wrap failure → `block_or_escalate` (cause = `legacy_role_wrap_failed`), preserve the legacy file, require manual migration.

## Worked example (the demo employee)

`./employees/emp-cs-demo-001.json` has `role_id: "电商客服"` (a Chinese alias). STEP 0:

1. file exists → `source=authoritative_file, reliability=high`
2. canonicalize "电商客服" → matches `customer-service-ecommerce.role.json` aliases → `employee.role.role_id = "customer-service-ecommerce"`, copy `industry=ecommerce`, `responsibility_tags=[customer_facing, tool_use, policy_application, complaint_handling, order_management]`
3. no caveat (clean match); STEP 1 role-filters on the canonical `customer-service-ecommerce`

## Anti-patterns

| Anti-pattern | K-rule | Failure mode |
|---|---|---|
| LLM silently accepts its own parse of a spoken description without showing the user | R2.2 | user never confirms; misinterpretation propagates |
| Fall through to inferred-fallback when an Employee_File exists but failed to parse | R1.5 | should be `block_or_escalate` |
| A later step rewrites `employee.role.role_id` | K17 / R6.5 | `unauthorized_role_id_mutation`; taint |
| Persist no `employee_provenance` | K17 | taint (atomic-fail) |
| `reliability=low` with empty/absent `caveat` | K17 / R4.7 | taint |
| Treat a bad role-catalog file as a hard error | role-catalog K3 | should be fail-soft skip + open_question |
