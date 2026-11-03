import type { AccountFeatureName } from './accountFeature'
import type { ListLoad, ListRequest, Paged } from './list'
import type { ResourceCeiling } from './resourceCeiling'

/**
 * Lifecycle state of a reseller, mirroring the backend's `ResellerStatus` enum. The API serializes
 * enums as camelCase strings (panel-wide `JsonStringEnumConverter`), so these are the camelCase
 * forms of the C# member names.
 */
export type ResellerStatus = 'active' | 'suspended'

/**
 * What an administrator lets a reseller sell, mirroring the backend's `ResellerEnvelopeDto`
 * field-for-field. The same shape is read back and sent (`POST /api/v1/resellers`,
 * `PUT /api/v1/resellers/{id}/envelope`): the envelope is a value the administrator replaces as a
 * whole, never patches. The server enforces every figure on each plan and account the reseller
 * writes; the SPA only shows and edits it.
 */
export interface ResellerEnvelope {
  /** How many accounts the reseller may own, whatever their state. */
  maxAccounts: number
  /** The disk the reseller may sell in all: the sum of its accounts' plan quotas, in megabytes. */
  diskPoolMb: number
  /** How many plans the reseller may define. */
  maxPlans: number
  /** The largest disk quota one of the reseller's plans may state, in megabytes. */
  maxDiskQuotaMb: number
  /** The most sites a plan of the reseller may allow one account. */
  maxSites: number
  /** The most databases a plan of the reseller may allow one account. */
  maxDatabases: number
  /** The most SFTP logins a plan of the reseller may allow one account. */
  maxSftpUsers: number
  /** The most FTPS logins a plan of the reseller may allow one account; zero lets it sell none. */
  maxFtpUsers: number
  /** The most cron entries a plan of the reseller may allow one account; zero lets it sell none. */
  maxCronEntries: number
  /** The most php-fpm workers a plan of the reseller may allow one pool. */
  maxPhpWorkersPerPool: number
  /** The CPU share a plan of the reseller may grant at most, in percent of one CPU. */
  cpuQuotaPercent: ResourceCeiling
  /** The memory a plan of the reseller may grant at most, in megabytes. */
  memoryMaxMb: ResourceCeiling
  /** The processes and threads a plan of the reseller may grant at most. */
  maxProcesses: ResourceCeiling
  /** The features the reseller may include in a plan, by name; a plan may sell a subset of these. */
  features: AccountFeatureName[]
}

/**
 * A reseller as the administrator's list sees it, mirroring the backend's `ResellerDto`
 * field-for-field. Usage is counted by the server so a screen never fetches every account to learn it.
 */
export interface Reseller {
  /** The reseller's identity. */
  id: string
  /** The reseller's unique name, which is also its login name. */
  name: string
  /** The address the reseller's invitation and password resets are mailed to. */
  contactEmail: string
  /** The reseller's current lifecycle state. */
  status: ResellerStatus
  /** The state as a reader reads it, already in the request's language; what the status badge prints. */
  statusName: string
  /** The instant the reseller was created, as an ISO-8601 string. */
  createdAt: string
  /** How many accounts the reseller owns right now, whatever their state. */
  accountCount: number
  /** How many plans the reseller owns right now. */
  planCount: number
  /** The disk allocated so far: the sum of its accounts' plan quotas, in megabytes. */
  diskAllocatedMb: number
}

/**
 * One reseller as its own page shows it, mirroring the backend's `ResellerDetailDto`: the list
 * shape plus the language its invitation was written in and its envelope.
 */
export interface ResellerDetail extends Reseller {
  /** The language the reseller's invitation was written in (`en`, `ru`, `hy`). */
  language: string
  /** What the reseller may sell. */
  envelope: ResellerEnvelope
}

/**
 * One reseller as a picker shows it, mirroring the backend's `ResellerChoiceDto`
 * (`GET /api/v1/resellers/choices`): the identity and the name, nothing a picker does not print.
 */
export interface ResellerChoice {
  /** The reseller's identity. */
  id: string
  /** The reseller's name. */
  name: string
}

/**
 * Where the open reseller picker's read of `GET /api/v1/resellers/choices` stands: in flight, or
 * ended the way every paged read ends ({@link ListLoad}), so "still asking" is never "nothing matched".
 */
export type ResellerChoicesLoad =
  | {
      /** The read is in flight; nothing from an earlier search is shown under the new one. */
      kind: 'loading'
    }
  | ListLoad<ResellerChoice>

/**
 * Request body for `POST /api/v1/resellers`, binding the backend's `CreateResellerCommand`
 * field-for-field. The same shape is what the reseller form emits, whether it creates or edits;
 * an edit sends only its envelope ({@link ChangeResellerEnvelopeRequest}).
 */
export interface CreateResellerRequest {
  /** The reseller's unique name, which is also its login name. */
  name: string
  /** The address the reseller's invitation is mailed to. */
  contactEmail: string
  /** The language the invitation is written in; one of the languages the panel ships. */
  language: string
  /** What the reseller may sell. */
  envelope: ResellerEnvelope
}

/** Request body for `PUT /api/v1/resellers/{id}/envelope`, binding `ChangeResellerEnvelopeCommand`. */
export interface ChangeResellerEnvelopeRequest {
  /** The envelope that replaces the reseller's current one. */
  envelope: ResellerEnvelope
}

/**
 * Typed access to the reseller endpoints.
 *
 * Called from Pinia stores only, never from a component (rules/vue.md). Everything except
 * {@link ResellersApi.getMine} is administrator-only on the server; a reseller's request for it is
 * refused there and the refusal is what the screen shows.
 */
export interface ResellersApi {
  /**
   * Lists one page of resellers. The request's `status` filter is sent as the endpoint's `status`.
   * @param request The page, size, search, sort and filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<Reseller>>

  /**
   * Lists the resellers a picker offers: the first page of those matching the search.
   * @param search Narrows by name; empty for the first resellers in name order.
   * @param pageSize How many choices to ask for; one of the sizes the panel accepts (10, 25, 50, 100).
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The matching resellers as identity and name, with how many match in all.
   */
  choices: (search: string, pageSize: number, signal?: AbortSignal) => Promise<Paged<ResellerChoice>>

  /**
   * Reads one reseller with its envelope and usage.
   * @param id The reseller's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller as the panel currently has it.
   */
  get: (id: string, signal?: AbortSignal) => Promise<ResellerDetail>

  /**
   * Reads the reseller the caller is: its own profile, envelope and usage.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The caller's own reseller.
   */
  getMine: (signal?: AbortSignal) => Promise<ResellerDetail>

  /**
   * Creates a reseller; the panel invites its login by mail.
   * @param request The reseller's name, contact address, language and envelope.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller as the panel created it.
   */
  create: (request: CreateResellerRequest, signal?: AbortSignal) => Promise<ResellerDetail>

  /**
   * Replaces a reseller's envelope. Lowering it below current usage is allowed: what exists keeps
   * working and new writes are refused.
   * @param id The reseller's identity.
   * @param request The new envelope.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller as it now stands.
   */
  changeEnvelope: (id: string, request: ChangeResellerEnvelopeRequest, signal?: AbortSignal) => Promise<ResellerDetail>

  /**
   * Suspends a reseller: its login stops signing in and its writes are refused. Its customers'
   * accounts keep running.
   * @param id The reseller's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller in its new state.
   */
  suspend: (id: string, signal?: AbortSignal) => Promise<Reseller>

  /**
   * Lifts a suspension.
   * @param id The reseller's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller in its new state.
   */
  reactivate: (id: string, signal?: AbortSignal) => Promise<Reseller>

  /**
   * Deletes a reseller; refused while it owns accounts or plans.
   * @param id The reseller's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the reseller was deleted; the panel answers 204 with no body.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>
}
