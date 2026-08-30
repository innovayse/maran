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
  /** What was done, as the backend's own action name. */
  action: string
  /** What it was done to. Never carries a secret — the backend is responsible for that. */
  subject: string
  /** The address the request came from. */
  ipAddress: string
  /** Whether the action succeeded. A failed sign-in is recorded as much as a successful one. */
  succeeded: boolean
}

/** The audit endpoints this SPA calls. */
export interface AuditApi {
  /** Lists the most recent entries, newest first. */
  list: (limit?: number, signal?: AbortSignal) => Promise<AuditEvent[]>
}
