/**
 * One environment assignment in the agent-managed region of an account's crontab, mirroring the
 * backend's `CronEnvironmentVariableDto` field-for-field.
 *
 * One type for reading and for writing, exactly as the module has it: the set is REPLACED whole
 * rather than merged, so what the panel sends is what the crontab will hold.
 */
export interface CronEnvironmentVariable {
  /**
   * The variable's name — uppercase letters, digits and underscores, not starting with a digit.
   *
   * `MAILTO` and `SHELL` are refused by the panel (R13): the agent writes both itself, one is an
   * outbound relay through the host's mail transfer agent and the other chooses the interpreter
   * every entry runs under. The SPA only ever HINTS at that; the refusal is the server's.
   */
  name: string

  /**
   * The value, written verbatim into a `NAME=value` line of the crontab.
   *
   * It is the customer's own text and may carry a credential, which is why the panel shows it back
   * to them and the backend keeps it out of every log line and audit row. An empty value is a real
   * assignment (`TZ=`), not an absence.
   */
  value: string
}

/**
 * Request body for `PUT /api/v1/cron-environment`, mirroring the backend's
 * `SetCronEnvironmentRequest`.
 *
 * `PUT`, not `PATCH`, and the verb is the warning: a name absent from {@link variables} is removed
 * from the crontab, and an empty list clears every managed assignment.
 */
export interface SetCronEnvironmentRequest {
  /** The account whose crontab is rewritten. */
  accountId: string

  /** The complete new set of assignments; an empty list clears them all. */
  variables: CronEnvironmentVariable[]
}
