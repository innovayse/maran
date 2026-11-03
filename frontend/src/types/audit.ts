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
  /** What was done, as the backend's machine-stable action name — what an operator greps a log by. */
  action: string
  /**
   * The action as an operator reads it, localized by the backend for the request's language.
   * Falls back to the machine name on the server for an action this panel has no entry for.
   */
  actionName: string
  /** What it was done to. Never carries a secret — the backend is responsible for that. */
  subject: string
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

/** The audit endpoints this SPA calls. */
export interface AuditApi {
  /**
   * Reads one page of the journal, searched, filtered and sorted by the panel.
   * @param request The page, size, search, sort and the action, outcome, from and to filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<AuditEvent>>
  /**
   * Lists the distinct actions the journal holds, for the action filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The actions with their localized names.
   */
  actions: (signal?: AbortSignal) => Promise<AuditActionChoice[]>
  /**
   * Lists the outcomes the journal can be filtered by, for the outcome filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The outcomes with their localized names.
   */
  outcomes: (signal?: AbortSignal) => Promise<AuditOutcomeChoice[]>
}
