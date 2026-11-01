import type { AccountResourceLimitKind } from './accountResourceSummary'
import type { ListRequest, Paged } from './list'

/**
 * One runtime ceiling of a plan, mirroring the backend's `ResourceCeilingDto`: CPU, memory or
 * processes an account may use at once (issue #133). "No ceiling" is its own case and never a zero;
 * `maximum` is meaningful only when `kind` is `limited`, and is 0 otherwise.
 */
export interface PlanCeiling {
  /** Whether the plan caps this resource at all. */
  kind: AccountResourceLimitKind
  /** The cap, in the unit the field it sits in names; 0 when `kind` is `unlimited`. */
  maximum: number
}

/**
 * A plan as the administrator's plans screen sees it, mirroring the backend's `PlanDto`
 * field-for-field. The name arrives already resolved for the request's language: a plan the panel
 * shipped is translated, one an operator made is shown as they typed it, and the SPA does not
 * tell the two apart (rules/vue.md: "Data comes from the backend").
 *
 * A wider shape than `Plan` in `account.ts`, which is what a plan picker needs and nothing more,
 * read from the picker's own capped endpoint.
 */
export interface ManagedPlan {
  /** The plan's identity. */
  id: string
  /** The plan's name, already in the request's language. */
  displayName: string
  /** Disk each account on the plan may use, in megabytes. */
  diskQuotaMb: number
  /** How many sites an account on the plan may create. */
  maxSites: number
  /** How many databases an account on the plan may create. */
  maxDatabases: number
  /** How many SFTP logins an account on the plan may create. */
  maxSftpUsers: number
  /** How many FTPS logins an account on the plan may create; zero sells none. */
  maxFtpUsers: number
  /** How many cron entries an account on the plan may keep; zero sells none. */
  maxCronEntries: number
  /** How many php-fpm workers one of an account's pools may run. */
  maxPhpWorkersPerPool: number
  /** The share of CPU time an account may use, in percent of one CPU (200 is two whole cores). */
  cpuQuotaPercent: PlanCeiling
  /** The most memory an account's processes may hold together, in megabytes. */
  memoryMaxMb: PlanCeiling
  /** The most processes and threads an account may run at once. */
  maxProcesses: PlanCeiling
  /**
   * How many accounts are on this plan right now. The server counts them so the screen never has
   * to fetch every account to learn it: it is what tells an administrator that an edit reaches
   * N customers, and that a plan with any cannot be deleted.
   */
  accountCount: number
}

/**
 * Request body for `POST /api/v1/plans` and `PUT /api/v1/plans/{id}`, binding the backend's
 * `CreatePlanCommand` and `UpdatePlanCommand` field-for-field. One shape for both because the
 * update is a full replacement, not a patch: the form always holds every field, and an absent
 * count could not be told from the zero that is a real product for FTPS logins and cron entries.
 * The plan's id is never in the body; the route carries it.
 */
export interface PlanRequest {
  /** The plan's name; unique across plans, compared without regard to case. */
  name: string
  /** Disk quota per account, in megabytes. */
  diskQuotaMb: number
  /** Sites per account. */
  maxSites: number
  /** Databases per account. */
  maxDatabases: number
  /** SFTP logins per account. */
  maxSftpUsers: number
  /** FTPS logins per account; zero sells none. */
  maxFtpUsers: number
  /** Cron entries per account; zero sells none. */
  maxCronEntries: number
  /** php-fpm workers per pool. */
  maxPhpWorkersPerPool: number
  /** The share of CPU time an account may use, in percent of one CPU (200 is two whole cores). */
  cpuQuotaPercent: PlanCeiling
  /** The most memory an account's processes may hold together, in megabytes. */
  memoryMaxMb: PlanCeiling
  /** The most processes and threads an account may run at once. */
  maxProcesses: PlanCeiling
}

/**
 * Typed access to the plan-management endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md). Every call is
 * administrator-only on the server; a customer's request is refused there and the refusal is
 * what the screen shows.
 */
export interface PlansApi {
  /**
   * Lists one page of plans with their limits and how many accounts are on each.
   * @param request The page, size, search (the name) and sort (`name`, `diskQuota`, `accounts`).
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page, in the order the panel sorted it, with the total across every page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<ManagedPlan>>

  /**
   * Reads one plan with its limits and account count.
   * @param id The plan's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plan; a missing one is refused by the panel with its own message.
   */
  get: (id: string, signal?: AbortSignal) => Promise<ManagedPlan>

  /**
   * Defines a new plan.
   * @param request The plan's name and limits.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plan as the panel created it, with no accounts on it.
   */
  create: (request: PlanRequest, signal?: AbortSignal) => Promise<ManagedPlan>

  /**
   * Replaces a plan's name and limits; the new limits apply to every account already on it.
   * @param id The plan to edit.
   * @param request The plan's new name and limits.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plan as it now stands.
   */
  update: (id: string, request: PlanRequest, signal?: AbortSignal) => Promise<ManagedPlan>

  /**
   * Deletes a plan; refused while any account is on it.
   * @param id The plan to delete.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once the plan was deleted.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<boolean>
}
