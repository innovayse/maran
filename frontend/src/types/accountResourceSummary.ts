/**
 * What an account holds against its plan, mirroring the backend's `AccountResourceSummaryDto` and
 * `AccountResourceUsageDto` (`GET /api/v1/accounts/{id}/resource-summary` for an administrator,
 * `GET /api/v1/accounts/me/resource-summary` for the customer who owns it).
 *
 * Every state is its own case with no number standing in for it: whether a count could be taken is
 * {@link ResourceCount} (a figure exists only on a `counted` reading), and whether the plan bounds
 * the resource is {@link ResourceCeiling} (a figure exists only on a `limited` ceiling). Enums
 * arrive as camelCase strings (panel-wide `JsonStringEnumConverter`).
 */

import type { ResourceCeiling } from './resourceCeiling'

/**
 * A kind of resource an account holds, mirroring `Maran.Sdk.Contracts.AccountResourceKind`. The
 * server lists only the kinds whose module is installed, so the SPA never draws a row for a module
 * this server lacks.
 */
export type AccountResourceKind =
  | 'sites'
  | 'databases'
  | 'sftpLogins'
  | 'ftpLogins'
  | 'cronEntries'
  | 'backups'
  | 'mailboxes'

/**
 * How many of a resource an account holds, mirroring `ResourceCountDto`. Exactly one of two shapes
 * told apart by `state`: `unavailable` is a reading the server could not take (cron's crontab lives
 * on the host, and the agent may not answer), which is not the same statement as "none" and carries
 * no number.
 */
export type ResourceCount =
  /** The owning module counted. */
  | {
      /** Discriminator: a reading was taken. */
      state: 'counted'
      /** How many the account holds; zero is a real count. */
      used: number
    }
  /** The count could not be taken. */
  | {
      /** Discriminator: no reading. */
      state: 'unavailable'
    }

/** One resource: how many the account holds and what its plan allows. */
export interface AccountResourceUsage {
  /** Which resource this row counts. */
  kind: AccountResourceKind
  /** The reading, or the statement that none could be taken. */
  count: ResourceCount
  /** The plan's allowance; `unlimited` is what the plan says (backups carry no plan limit), not a value that failed to arrive. */
  limit: ResourceCeiling
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
