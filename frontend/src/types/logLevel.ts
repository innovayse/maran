/**
 * The panel's runtime log level (issue #131), from the angles the monitoring screen needs it: what
 * the panel reports, what a change sends, and the endpoint that carries both (rules/vue.md "Types" —
 * one domain per file).
 *
 * The screen offers exactly the `choices` the panel sent; the union below only types the wire value,
 * the way `SmtpSecurity` types the mail settings' enum, so `maran api` can hold the request to the
 * command it binds.
 */

/** A level as the panel's JSON spells it (`PanelLogLevel`, camelCase). */
export type PanelLogLevel = 'verbose' | 'debug' | 'information' | 'warning' | 'error' | 'fatal'

/** One level the panel offers, as `GET /api/v1/log-level` reports it (`LogLevelChoiceDto`). */
export interface LogLevelChoice {
  /** The level's machine value, as a change request names it. */
  level: PanelLogLevel
  /** What the level is called, already in the reader's language. */
  levelDisplayName: string
}

/** The panel's log level as `GET /api/v1/log-level` reports it (`LogLevelDto`). */
export interface LogLevel {
  /** The level the running logger writes at — the panel's observation, not a stored choice. */
  level: PanelLogLevel
  /** The running level's name, already in the reader's language. */
  levelDisplayName: string
  /** Every level an administrator may choose. */
  choices: LogLevelChoice[]
}

/** The body of `PUT /api/v1/log-level`. */
export interface ChangeLogLevelRequest {
  /** The level to apply, one of the `choices` the panel offered. */
  level: PanelLogLevel
}

/** Public surface of the log-level API composable. */
export interface LogLevelApi {
  /** Reads the running level and the levels on offer. */
  get: (signal?: AbortSignal) => Promise<LogLevel>
  /** Applies a level, now and after every restart. */
  change: (request: ChangeLogLevelRequest, signal?: AbortSignal) => Promise<boolean>
}
