/**
 * A plan as the administrator's plans screen sees it, mirroring the backend's `PlanDto`
 * field-for-field. The name arrives already resolved for the request's language: a plan the panel
 * shipped is translated, one an operator made is shown as they typed it, and the SPA does not
 * tell the two apart (rules/vue.md: "Data comes from the backend").
 *
 * A wider shape than `Plan` in `account.ts`, which is what the account form's plan picker needs
 * and nothing more. Both are read from endpoints that answer with this same record.
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
   * Lists every plan with its limits and how many accounts are on it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plans, in the order the panel reports them.
   */
  list: (signal?: AbortSignal) => Promise<ManagedPlan[]>

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
