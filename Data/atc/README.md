# ATC JSON Packs

This folder contains the JSON-driven intent, flow, and template packs that drive
deterministic ATC session behavior. Update these files to tweak intent matching,
state transitions, and response templates without touching C# code.

## intents.json
Defines intent classification rules.

Fields:
- `default_threshold`: fallback confidence threshold (0.0-1.0).
- `intents[]`:
  - `id`: unique intent ID (e.g., `REQUEST_IFR_CLEARANCE`).
  - `required_slots`: slot names required for a full match (e.g., `runway`).
  - `allowed_phases`: phases where this intent can be considered.
  - `score_rules[]`:
    - `id`: optional rule ID.
    - `keywords`: phrases that boost the intent score when matched.
    - `regex`: regex patterns (case-insensitive) that boost the score.
    - `boost`: score increment when the rule matches.
    - `expected_next_boost`: extra boost when the intent is expected next.

## flows.json
Defines session phases, allowed intents, transitions, and role-to-phase mapping.

Fields:
- `role_phase_map`: map controller roles to phase IDs (e.g., `ground -> ground`).
- `phases[]`:
  - `id`: phase ID (`clearance`, `ground`, `tower`, `departure`, `approach`, `center`).
  - `allowed_intents`: intent IDs allowed in this phase.
  - `default_controller_role`: controller role for TTS/prompting (e.g., `tower`).
  - `expected_next_intents`: used for intent scoring boosts.
  - `fallback_template`: template ID for fallback responses.
  - `transitions[]`:
    - `intent`: intent ID that triggers the transition.
    - `required_slots`: slots required before this transition can fire.
    - `requires_pending_handoff`: require an active pending handoff to match.
    - `next_state`: next phase ID (optional).
    - `atc_action`: action label used to select templates.
    - `set_pending_handoff`: if true, set pending handoff from `handoff`.
    - `commit_pending_handoff`: if true, transition to pending handoff phase.
    - `handoff`: optional `{ role, frequency_mhz }` data.

## templates.json
Maps phase+intent+action to deterministic response templates.

Fields:
- `templates[]`:
  - `id`: optional template ID (used for direct references).
  - `phase`: phase ID.
  - `intent`: intent ID.
  - `atc_action`: action label.
  - `text`: response text with `{slot}` placeholders.
  - `variants`: optional array of alternative phrasings.
  - `requires_readback`: whether a readback is required.
  - `readback_items`: list of readback items (e.g., `runway`, `squawk`).

Common slots used by templates:
- `next_role`, `next_freq`: pending handoff target used in handoff instructions.
- `facility`, `role`: used in check-in acknowledgments.

## How to edit
1. Update intent keywords/rules in `intents.json`.
2. Adjust transitions or handoffs in `flows.json`.
3. Edit template text or add variants in `templates.json`.
4. Restart AeroAI to reload the packs (or restart the UI).
