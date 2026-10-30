import type { AccountResourceSummary } from './accountResourceSummary'
import type { ListLoad, ListRequest, Paged } from './list'

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
 * One account as a picker or an owner name shows it, mirroring the backend's account choice row
 * (`GET /api/v1/accounts/choices`): the identity and the short name, nothing a picker does not
 * print.
 */
export interface AccountOption {
  /** The account's identity. */
  id: string
  /** The account's unique, Linux-username-safe short name. */
  name: string
}

/**
 * Where the open account picker's read of `GET /api/v1/accounts/choices` stands: in flight, or ended
 * the way every paged read ends ({@link ListLoad}). A union rather than a nullable page, so "still
 * asking" can never be mistaken for "nothing matched".
 */
export type AccountChoicesLoad =
  | {
      /** The read is in flight; nothing from an earlier search is shown under the new one. */
      kind: 'loading'
    }
  | ListLoad<AccountOption>

/**
 * One account as its own page shows it, mirroring the backend's
 * `AccountDetailDto` field-for-field: the list shape plus its plan by name and
 * its owner's contact address.
 */
export interface AccountDetail extends Account {
  /** The plan's name, already in the request's language. */
  planDisplayName: string
  /**
   * The account's own contact address, which its login's invitation and
   * password resets follow. `null` only for an account older than the field.
   */
  ownerEmail: string | null
}

/**
 * Request body for `PUT /api/v1/accounts/{id}/plan`, binding the backend's
 * `ChangeAccountPlanCommand`.
 */
export interface ChangeAccountPlanRequest {
  /** The plan the account is to be sold from now on. */
  planId: string
}

/**
 * Request body for `PUT /api/v1/accounts/{id}/owner-email`, binding the
 * backend's `ChangeAccountOwnerEmailCommand`.
 */
export interface ChangeAccountOwnerEmailRequest {
  /** The new owner address. */
  ownerEmail: string
}

/**
 * A plan an account can be created against, as the plan picker reads it
 * (`GET /api/v1/plans/choices`, the backend's `PlanChoiceDto`): the identity to send back, the
 * name, and the two limits the picker prints beside it. The display name arrives already
 * localized: plans are server-side reference data, and the SPA never translates or invents them
 * (rules/vue.md).
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
}

/**
 * Where the open plan picker's read of `GET /api/v1/plans/choices` stands: in flight, or ended the
 * way every paged read ends ({@link ListLoad}), so "still asking" is never "nothing matched".
 */
export type PlanChoicesLoad =
  | {
      /** The read is in flight; nothing from an earlier search is shown under the new one. */
      kind: 'loading'
    }
  | ListLoad<Plan>

/**
 * Request body for `POST /api/v1/accounts`, binding the backend's
 * `CreateAccountCommand` field-for-field.
 */
export interface CreateAccountRequest {
  /** The account's unique, Linux-username-safe short name. */
  name: string
  /** The account's primary domain. */
  primaryDomain: string
  /** The id of the plan bounding this account's resource limits. */
  planId: string
  /**
   * The account's own contact address, carried to Identity and stored on the
   * login the backend creates for it — what an administrator's resend of the
   * invitation would be addressed to.
   */
  ownerEmail: string
}

/**
 * The plan section of {@link MyAccount}, mirroring the backend's
 * `MyAccountPlanDto` field-for-field. A distinct shape from {@link Plan}: this
 * one is always read embedded in a caller's own account and carries two limits
 * the plan-picker list has never needed.
 */
export interface MyAccountPlan {
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
  /** How many FTPS logins the plan allows. */
  maxFtpUsers: number
  /** How many cron entries the plan allows in the account's crontab. */
  maxCronEntries: number
}

/**
 * A customer's own view of the account they own, mirroring the backend's
 * `MyAccountDto` field-for-field. Deliberately carries no owner address: that
 * field belongs to `GET /api/v1/accounts/me`'s contract exactly as documented
 * server-side, and this type mirrors it rather than widening it.
 */
export interface MyAccount {
  /** The account's identity. */
  id: string
  /** The account's unique, Linux-username-safe short name. */
  name: string
  /** The account's primary domain. */
  primaryDomain: string
  /** The account's current lifecycle state. */
  status: AccountStatus
  /** The limits of the plan this account is created against. */
  plan: MyAccountPlan
}

/**
 * Typed access to the hosting accounts endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface AccountsApi {
  /**
   * Lists the plans a picker offers: the first page, alphabetical, of those matching the search.
   * @param search Narrows by name; empty for the first plans in alphabetical order.
   * @param pageSize How many choices to ask for; one of the sizes the panel accepts (10, 25, 50, 100).
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The matching plans, with how many match in all.
   */
  planChoices: (search: string, pageSize: number, signal?: AbortSignal) => Promise<Paged<Plan>>

  /**
   * Reads one plan, for text that names a plan the open page of choices may not hold.
   * @param id The plan's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plan; its wider fields beyond {@link Plan} are ignored.
   */
  getPlan: (id: string, signal?: AbortSignal) => Promise<Plan>

  /**
   * Reads the account the caller owns: its identity, status, and its plan's
   * limits.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The caller's own account.
   */
  getMine: (signal?: AbortSignal) => Promise<MyAccount>

  /**
   * Lists one page of hosting accounts. The request's `plan` and `status` filters are sent as the
   * endpoint's `planId` and `status`.
   * @param request The page, size, search, sort and filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<Account>>

  /**
   * Lists the accounts a picker offers: the first page of those matching the search.
   * @param search Narrows by name; empty for the first accounts in name order.
   * @param pageSize How many choices to ask for; one of the sizes the panel accepts (10, 25, 50, 100).
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The first page of matching accounts as identity and name.
   */
  choices: (search: string, pageSize: number, signal?: AbortSignal) => Promise<Paged<AccountOption>>

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
  get: (id: string, signal?: AbortSignal) => Promise<AccountDetail>

  /**
   * Moves an account to another plan.
   * @param id The account's identity.
   * @param request The plan to move it to.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account as it now stands.
   */
  changePlan: (id: string, request: ChangeAccountPlanRequest, signal?: AbortSignal) => Promise<AccountDetail>

  /**
   * Changes an account's owner address, and with it the address its login is mailed at.
   * @param id The account's identity.
   * @param request The new address.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account as it now stands.
   */
  changeOwnerEmail: (
    id: string,
    request: ChangeAccountOwnerEmailRequest,
    signal?: AbortSignal,
  ) => Promise<AccountDetail>

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

  /**
   * Reads what one account holds against its plan.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per resource kind this server counts.
   */
  getResourceSummary: (id: string, signal?: AbortSignal) => Promise<AccountResourceSummary>

  /**
   * Reads what the caller's own account holds against its plan.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per resource kind this server counts.
   */
  getMyResourceSummary: (signal?: AbortSignal) => Promise<AccountResourceSummary>
}
