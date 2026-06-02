# customer_realistic

Default **role profile** that the evaluation-expert agent itself plays in STEP 3 to impersonate a realistic customer. The agent's OWN LLM brain (the same one that runs STEP 1.5 / STEP 4 / STEP 8 / STEP 9) reads `system_prompt.md` here, fills the placeholders from the enriched test case + the in-progress execution trace, and produces one `SimulatorDecision` per turn.

The agent then forwards `decision.next_utterance` to the runtime driver subprocess (which talks to the evaluatee over WebSocket / JWT) and appends the full decision to `execution_trace.simulator_trail` for audit.

> ⚠️ This directory does **not** contain an executable. There is **no** subprocess, **no** external LLM key, **no** `decide.py`. The simulator is a prompt template + manifest; the LLM that consumes it is the host evaluation-expert agent's own brain.

## Persona summary

- Acts according to `customer_persona` (personality, communication style, patience).
- Pursues `goal.primary` (and optionally `secondary`); abandons the conversation if the agent falls below `goal.bottom_line`.
- Updates `internal_emotion` and `perceived_progress` each turn based on the agent's latest reply.
- Stops on its own when `stop_conditions` are met, without exhausting `turn_budget.hard_max_turns`.

## Files

| File | Purpose | Status |
|---|---|---|
| `simulator.json` | Manifest validated against `runtime-schemas/simulator.schema.json` | ✅ committed |
| `system_prompt.md` | LLM system-prompt template with `{{placeholders}}` filled by the host agent each turn | ✅ committed |

## Per-turn flow (executed inside the host agent)

For each customer turn `n` (0-indexed):

1. **Build the prompt context** by reading from the enriched test case and the execution trace so far:
   - `customer_persona.*`, `context`, `goal.*`, `stop_conditions.*` — from `enriched_test_case.input`.
   - `dialog_so_far` — `execution_trace.dialog_turns` rendered as `customer: …` / `agent: …` lines.
   - `current_emotion` — derived from the last entry in `simulator_trail` (or `initial_emotion` if empty), shifted along the emotion ladder by `emotion_shift` if present.
   - `effective_max_turns` = `min(turn_budget.hard_max_turns, evaluation_context.global_turn_cap)`.
2. **Turn 0 short-circuit**: emit `next_utterance = enriched_test_case.input.opening_message` verbatim with `should_continue=true`, `internal_emotion=initial_emotion`, `perceived_progress="none"`. Do NOT consult the LLM on turn 0 — there is nothing for the customer to react to yet.
3. **Turn ≥ 1**: render `system_prompt.md` with the context, ask the host LLM to return a JSON object matching `runtime-schemas/simulator_decision.schema.json`, parse and validate it.
4. **Honor invariants** before accepting the decision:
   - `should_continue=false` ⇒ `stop_reason` MUST be one of `goal_achieved` / `bottom_line_violated` / `customer_gave_up` / `deadlock_detected`; `next_utterance` MAY be a final remark or empty string.
   - `should_continue=true` ⇒ `stop_reason` MUST be absent (or null); `next_utterance` MUST be non-empty.
   - `violated_bottom_line=true` ⇒ `stop_reason=bottom_line_violated` and `should_continue=false`.
5. **Send to driver**: write `{"action":"send","turn_index":n,"text":decision.next_utterance,"decision":decision}` to the driver's stdin and read the next `evaluatee_turn` event from the driver's stdout.
6. **Stop**: when `should_continue=false` (or when the loop hits `effective_max_turns`), write `{"action":"end","decision":finalDecision,"termination":{...}}` to the driver and let it write the trace file.

## Authoring `system_prompt.md`

The prompt template is intentionally editable by non-engineers (PMs / domain experts). Use Mustache-style `{{placeholder}}` only; the host agent fills:

- `{{customer_persona.*}}`
- `{{context}}` (rendered as a short paragraph)
- `{{goal.*}}`
- `{{stop_conditions.*}}`
- `{{current_emotion}}` (running absolute state)
- `{{dialog_so_far}}` (rendered as `agent: …` / `customer: …` lines)
- `{{effective_max_turns}}`

Do not embed Python or executable logic in the template; if you need branching, push it into the host agent's STEP 3 LLM workflow, not into this file.

## Adding alternative personas

To add `customer_calm`, `customer_aggressive`, etc:

1. Drop a sibling directory `simulators/<new_id>/`.
2. Copy `simulator.json` and adjust `simulator_id` + `version`.
3. Rewrite `system_prompt.md` for the new persona's voice.

No contract or projection edits required. No code is added — the host agent's LLM is what consumes the new prompt.

## Emotion ladder reference

The host agent maintains `current_emotion` as an absolute state on a 7-step ladder:

```
angry → frustrated → anxious → skeptical → neutral → curious → satisfied
```

`emotion_shift` from the previous decision is applied as a delta:

| Shift | Movement |
|---|---|
| `more_upset` | one step left (toward `angry`) |
| `calmer` | one step right (toward `satisfied`) |
| `unchanged` (or absent) | hold position |

`initial_emotion` from the test case sets the starting position before turn 1.
