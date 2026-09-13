import { useApi } from '../useApi'
import type { AuditApi, AuditEvent } from '../../types/audit'

/** The endpoint the append-only audit journal is read through. Administrators only. */
const AUDIT_PATH = '/api/v1/audit'

/** How many entries to ask for when the caller does not say. Matches the backend's own default. */
const DEFAULT_LIMIT = 100

/**
 * The audit journal's HTTP surface. Called from a store, never from a component
 * (rules/vue.md: "useApi and the apis composables are called from stores only").
 * @returns The audit endpoints, bound to the shared request pipeline.
 */
export const useAuditApi = (): AuditApi => {
  const api = useApi()

  /**
   * Lists the most recent audit entries.
   * @param limit How many to return; the backend refuses a value outside its own range.
   * @param signal Abort signal, so a page left before the response arrives cancels the request.
   * @returns The entries, newest first.
   */
  const list = async (limit: number = DEFAULT_LIMIT, signal?: AbortSignal): Promise<AuditEvent[]> => {
    return api.get<AuditEvent[]>(`${AUDIT_PATH}?limit=${limit}`, signal)
  }

  return { list }
}
