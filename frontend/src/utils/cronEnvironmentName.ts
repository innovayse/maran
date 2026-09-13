/**
 * The names the panel's cron module refuses in an account's crontab preamble, and the one question
 * the environment editor asks about a name.
 *
 * **This is a hint, never a decision (R13).** The refusal is enforced by
 * `CronEnvironmentVariableValidator` in the Cron module and again by the agent, and it stays there:
 * what this buys is that an operator who types one of these names is told so before they press
 * Save, instead of after a round trip. The editor therefore renders the hint and still lets the
 * request go — the server's answer is the authoritative one, and a client that refused on its own
 * would be a second copy of an authorization rule, which is a second place for it to be wrong.
 *
 * It is also deliberately on the permissive side of the module in both directions. It gates
 * nothing, so it can only ever be advice; and it matches case-insensitively, so a lowercase
 * spelling earns the same warning even though the module refuses that one for a different reason
 * (its alphabet). Advising about a little more than the server refuses costs an operator one
 * sentence; advising about less would let them submit a name that was never going to be accepted.
 */

/**
 * The names the agent writes itself and no customer may set.
 *
 * `MAILTO` is an outbound-relay primitive: a customer who could set it would have mail leaving the
 * host through its mail transfer agent, addressed wherever they chose. `SHELL` chooses the
 * interpreter every one of that account's entries runs under — including entries created before
 * they changed it. Everything else passes: `PATH`, `TZ`, `CRON_TZ` and any other name the module's
 * alphabet accepts.
 */
export const RESERVED_CRON_ENVIRONMENT_NAMES: readonly string[] = ['MAILTO', 'SHELL']

/**
 * Whether the panel is going to refuse this name, as far as this SPA can tell.
 * @param name The variable name as the operator typed it.
 * @returns True when the name is one the agent manages itself.
 */
export const isReservedCronEnvironmentName = (name: string): boolean => {
  const upper = name.toUpperCase()
  return RESERVED_CRON_ENVIRONMENT_NAMES.some((reserved) => {
    return reserved === upper
  })
}
