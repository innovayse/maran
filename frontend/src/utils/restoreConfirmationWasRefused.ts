/**
 * The problem codes that mean the RESTORE was refused because of what the operator typed.
 *
 * `RestoreConfirmationMismatch` is a confirmation that did not match the account's system user
 * name; `RestoreConfirmationRequired` is an empty one. Both are answered before anything is
 * touched, and both are fixed by typing again — which is what makes them the only two failures
 * the panel may tell the operator to correct.
 *
 * Every other untouched ending is NOT one of them: a copy the agent refused as unusable (a
 * digest that did not match, a tampered or truncated artifact), a backup that never completed,
 * a backup already running, an account the panel cannot find, a stream that dropped. Retyping the
 * account name answers none of those, and a screen that asks for it during a recovery sends the
 * operator down a dead end at the worst possible moment.
 *
 * This is a behaviour list, not a text list: the message itself is the backend's, already
 * localized, and is rendered verbatim (rules/vue.md). What branches here is only which of the
 * panel's own two "what to do next" sentences is honest.
 */
const CONFIRMATION_CODES: readonly string[] = [
  'RestoreConfirmationMismatch',
  'RestoreConfirmationRequired',
]

/**
 * Whether a failed restore was refused over the typed confirmation.
 *
 * An unrecognised code answers `false`, and that direction is deliberate: the `false` branch tells
 * the operator that nothing was changed and that retyping will not help, which is true of every
 * failure this function does not name, while the `true` branch instructs them to retype and is
 * true of exactly two. Defaulting to the instruction would make every new failure code a dead end
 * the day it is added — which is the defect this split exists to remove.
 * @param code The machine-stable problem code the panel answered with.
 * @returns True when the operator's confirmation is the thing to correct.
 */
export const restoreConfirmationWasRefused = (code: string): boolean => {
  return CONFIRMATION_CODES.includes(code)
}
