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
}

/** Whether a recorded action worked, as the journal's `outcome` filter names it on the wire. */
export type AuditOutcome = 'succeeded' | 'failed'

/** Every {@link AuditOutcome}, in the order the filter offers them. */
export const AUDIT_OUTCOMES: readonly AuditOutcome[] = ['succeeded', 'failed']

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
}
