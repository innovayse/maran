import type { ApiDownload } from './api'
import type { ListRequest, Paged } from './list'

/**
 * The audit journal's shapes. Mirrors `AuditEventDto` on the backend: the SPA never
 * reshapes what the panel sends, it renders it (rules/vue.md).
 */

/** One entry of the append-only journal: who did what, when, from where, and whether it worked. */
export interface AuditEvent {
  /** Stable identifier of the entry, used as the row key. */
  id: string
  /** When the action happened, as an ISO-8601 instant. */
  occurredAt: string
  /** The login name of whoever performed it. */
  actorUsername: string
  /**
   * The login name of the administrator who was viewing the panel as the actor when this was done,
   * or `null` when the actor acted for themselves. The actor is still who the entry is about; this
   * names the person at the keyboard.
   */
  impersonatorUsername: string | null
  /** What was done, as the backend's machine-stable action name — what an operator greps a log by. */
  action: string
  /**
   * The action as an operator reads it, localized by the backend for the request's language.
   * Falls back to the machine name on the server for an action this panel has no entry for.
   */
  actionName: string
  /** What it was done to, as the journal stores it. Never carries a secret — the backend is responsible for that. */
  subject: string
  /**
   * What it was done to, as an operator reads it, localized by the backend for the request's
   * language: a sentence for an entry whose stored subject is a machine line (a chain verdict, a
   * retention run, a threshold change), and the stored subject itself for every other entry.
   */
  subjectName: string
  /** The address the request came from. */
  ipAddress: string
  /** Whether the action succeeded. A failed sign-in is recorded as much as a successful one. */
  succeeded: boolean
  /** The outcome in words, in the request's language: what the outcome badge prints. */
  outcomeName: string
}

/**
 * One outcome the journal can be filtered by, as the outcome filter offers it: the value the filter
 * sends back, and the name an administrator reads, localized by the backend. The list itself comes
 * from the panel, so the bundle holds no copy of the vocabulary.
 */
export interface AuditOutcomeChoice {
  /** The outcome as the journal's `outcome` filter binds it, sent back as that filter. */
  outcome: string
  /** The outcome as an administrator reads it, in the request's language. */
  outcomeName: string
}

/**
 * One action the journal holds, as the action filter offers it: the machine-stable name the filter
 * sends, and the name an operator reads, localized by the backend.
 */
export interface AuditActionChoice {
  /** The machine-stable action, sent back as the `action` filter. */
  action: string
  /** The action as an operator reads it, in the request's language. */
  actionName: string
}

/** A file format the journal can be exported in, as the export endpoint's `format` binds it. */
export type AuditExportFormat = 'csv' | 'json'

/**
 * How long the journal keeps its entries, as `GET /api/v1/audit/retention` reports it. The bounds
 * come from the panel with the setting, so the screen holds no copy of them.
 */
export interface AuditRetention {
  /** Days an entry is kept before the daily run may purge it, or `null` to keep entries indefinitely. */
  retentionDays: number | null
  /** The fewest days the panel accepts for {@link retentionDays}. */
  minimumDays: number
  /** The most days the panel accepts for {@link retentionDays}. */
  maximumDays: number
  /** When the daily retention run last ran, as an ISO-8601 instant, or `null` if it has not yet. */
  lastRunAt: string | null
}

/** The tamper-evidence chain's state, as `GET /api/v1/audit/chain` reports it. */
export interface AuditChainStatus {
  /** Entries already linked into the chain. */
  sealedRows: number
  /** Entries written but not yet linked; the maintenance run seals them within the hour. */
  unsealedRows: number
  /** Position of the newest sealed entry, or `null` while nothing is sealed. */
  lastChainIndex: number | null
  /** When the newest sealed entry was written, as an ISO-8601 instant, or `null` while nothing is sealed. */
  lastSealedAt: string | null
  /** Position up to which retention has purged entries; verification starts after it. */
  purgedThroughIndex: number
}

/** The answer of a chain verification, as `POST /api/v1/audit/chain/verification` returns it. */
export interface AuditChainVerification {
  /** True when every checked entry matched the chain. */
  intact: boolean
  /** How many entries were checked. */
  rowsChecked: number
  /** Position of the first entry that did not match, or `null` when the chain is intact. */
  firstBrokenChainIndex: number | null
  /** What did not match, as the backend's machine-stable name; `None` when intact. Behaviour only, never a text key. */
  break: string
  /** When the verification ran, as an ISO-8601 instant. */
  verifiedAt: string
}

/**
 * Which part of the journal a screen reads. `all` is the administrator's whole journal; `reseller`
 * is a reseller's own part of it (the entries about its accounts and its login, issue #370). The two
 * answer in one shape from two endpoints, and the panel — not the SPA — decides what each holds.
 */
export type AuditScope = 'all' | 'reseller'

/** The audit endpoints this SPA calls. */
export interface AuditApi {
  /**
   * Reads one page of the journal, searched, filtered and sorted by the panel.
   * @param request The page, size, search, sort and the action, outcome, from and to filters.
   * @param scope Whose journal to read: the administrator's whole one or a reseller's part of it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  list: (request: ListRequest, scope: AuditScope, signal?: AbortSignal) => Promise<Paged<AuditEvent>>
  /**
   * Lists the distinct actions the journal holds, for the action filter.
   * @param scope Whose journal to read the actions of.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The actions with their localized names.
   */
  actions: (scope: AuditScope, signal?: AbortSignal) => Promise<AuditActionChoice[]>
  /**
   * Lists the outcomes the journal can be filtered by, for the outcome filter.
   * @param scope Whose journal the filter is for.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The outcomes with their localized names.
   */
  outcomes: (scope: AuditScope, signal?: AbortSignal) => Promise<AuditOutcomeChoice[]>
  /**
   * Downloads the journal, narrowed by the same filters the list takes.
   * @param request The search and the filters (action, outcome, from, to, actor, account); page and sort are ignored.
   * @param format The file format asked for.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The file and the name the panel proposed for it.
   */
  exportJournal: (request: ListRequest, format: AuditExportFormat, signal?: AbortSignal) => Promise<ApiDownload>
  /**
   * Reads how long the journal keeps its entries.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The retention setting with its bounds.
   */
  retention: (signal?: AbortSignal) => Promise<AuditRetention>
  /**
   * Replaces the retention setting.
   * @param retentionDays Days to keep entries, or `null` to keep them indefinitely.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel has stored it.
   */
  saveRetention: (retentionDays: number | null, signal?: AbortSignal) => Promise<void>
  /**
   * Reads the tamper-evidence chain's state.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The sealed and unsealed counts and the purge position.
   */
  chainStatus: (signal?: AbortSignal) => Promise<AuditChainStatus>
  /**
   * Asks the panel to verify the whole chain now.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The verdict.
   */
  verifyChain: (signal?: AbortSignal) => Promise<AuditChainVerification>
}
