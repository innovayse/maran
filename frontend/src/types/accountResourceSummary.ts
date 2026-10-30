/**
 * What an account holds against its plan, mirroring the backend's `AccountResourceSummaryDto` and
 * `AccountResourceUsageDto` (`GET /api/v1/accounts/{id}/resource-summary` for an administrator,
 * `GET /api/v1/accounts/me/resource-summary` for the customer who owns it).
 *
 * Every state is an explicit field, never a `null` standing for one: whether a count could be taken
 * is {@link AccountResourceCountState}, and whether the plan bounds the resource is
 * {@link AccountResourceLimitKind}. Enums arrive as camelCase strings (panel-wide
 * `JsonStringEnumConverter`).
 */

/**
 * A kind of resource an account holds, mirroring `Maran.Sdk.Contracts.AccountResourceKind`. The
 * server lists only the kinds whose module is installed, so the SPA never draws a row for a module
 * this server lacks.
 */
export type AccountResourceKind = 'sites' | 'databases' | 'sftpLogins' | 'ftpLogins' | 'cronEntries' | 'backups'

/**
 * Whether a resource could be counted, mirroring `Maran.Sdk.Contracts.AccountResourceCountState`.
 * `unavailable` is a reading the server could not take (cron's crontab lives on the host, and the
 * agent may not answer), which is not the same statement as "none".
 */
export type AccountResourceCountState = 'counted' | 'unavailable'

/**
 * Whether the account's plan bounds a resource, mirroring
 * `Maran.Modules.Accounts.Domain.Enums.ResourceLimitKind`. `unlimited` is what the plan says
 * (backups carry no plan limit), not a value that failed to arrive.
 */
export type AccountResourceLimitKind = 'limited' | 'unlimited'

/** One resource: how many the account holds and what its plan allows. */
export interface AccountResourceUsage {
  /** Which resource this row counts. */
  kind: AccountResourceKind
  /** Whether {@link used} is a reading. */
  state: AccountResourceCountState
  /** How many the account holds; meaningful only when {@link state} is `counted`. */
  used: number
  /** Whether {@link limit} bounds the resource. */
  limitKind: AccountResourceLimitKind
  /** The plan's allowance; meaningful only when {@link limitKind} is `limited`. */
  limit: number
}

/** The resources one account holds against its plan. */
export interface AccountResourceSummary {
  /** The account the summary describes. */
  accountId: string
  /** One row per resource kind this server's modules count, in the order the server sends. */
  resources: AccountResourceUsage[]
}

/**
 * Where the SPA's read of one summary stands. Each outcome is its own variant, so neither "not read
 * yet" nor "no message" is spelled as a `null`, and a request that never reached the server cannot
 * leave the card waiting forever.
 */
export type AccountResourceSummaryRead =
  /** The request is in flight. */
  | { state: 'loading' }
  /** The server answered with the summary. */
  | { state: 'loaded'; summary: AccountResourceSummary }
  /** The server refused; `message` is its own, already localized. */
  | { state: 'refused'; message: string }
  /** No answer arrived: the API could not be reached. */
  | { state: 'unreachable' }
