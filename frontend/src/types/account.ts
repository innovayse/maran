/**
 * Lifecycle state of a hosting account, mirroring the backend's
 * `Maran.Modules.Accounts.Domain.AccountStatus` enum. The API serializes
 * enums as camelCase strings (panel-wide `JsonStringEnumConverter`), so the
 * values here are the camelCase form of the C# member names, not the C#
 * spelling itself.
 */
export type AccountStatus = 'active' | 'suspended'

/**
 * Outward, list-shaped view of a hosting account, mirroring the backend's
 * `AccountDto` field-for-field.
 */
export interface Account {
  /** The account's identity. */
  id: string
  /** The account's unique, Linux-username-safe short name. */
  name: string
  /** The account's primary domain. */
  primaryDomain: string
  /** The id of the plan bounding this account's resource limits. */
  planId: string
  /** The account's current lifecycle state. */
  status: AccountStatus
  /** The instant the account was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/accounts`, mirroring the backend's
 * `CreateAccountRequest` field-for-field.
 */
/**
 * A plan an account can be created against, as the panel reports it. The display
 * name arrives already localized: plans are server-side reference data, and the
 * SPA never translates or invents them (rules/vue.md).
 */
export interface Plan {
  /** The plan's identity, submitted with a new account. */
  id: string
  /** The plan's name, already in the request's language. */
  displayName: string
  /** Disk the plan allows, in megabytes. */
  diskQuotaMb: number
  /** How many sites the plan allows. */
  maxSites: number
  /** How many databases the plan allows. */
  maxDatabases: number
  /** How many SFTP logins the plan allows. */
  maxSftpUsers: number
}

/**
 * Request body for `POST /api/v1/accounts`, mirroring the backend's
 * `CreateAccountRequest` field-for-field.
 */
export interface CreateAccountRequest {
  /** The account's unique, Linux-username-safe short name. */
  name: string
  /** The account's primary domain. */
  primaryDomain: string
  /** The id of the plan bounding this account's resource limits. */
  planId: string
}

/**
 * Typed access to the hosting accounts endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface AccountsApi {
  /** Lists the plans an account can be created against. */
  listPlans: (signal?: AbortSignal) => Promise<Plan[]>

  /**
   * Lists every hosting account.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The accounts the panel currently has.
   */
  list: (signal?: AbortSignal) => Promise<Account[]>

  /**
   * Creates a new hosting account row.
   * @param request The account's name, primary domain, and plan.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The created account.
   */
  create: (request: CreateAccountRequest, signal?: AbortSignal) => Promise<Account>

  /**
   * Reads one account.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account, as the panel currently has it.
   */
  get: (id: string, signal?: AbortSignal) => Promise<Account>

  /**
   * Suspends an account: the panel asks the agent to stop it, then records the new state.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account in its new state.
   */
  suspend: (id: string, signal?: AbortSignal) => Promise<Account>

  /**
   * Lifts a suspension.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account in its new state.
   */
  reactivate: (id: string, signal?: AbortSignal) => Promise<Account>

  /**
   * Deletes an account, its system user and its home directory.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The number of bytes the deletion reclaimed, as the agent reported it.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<number>
}
