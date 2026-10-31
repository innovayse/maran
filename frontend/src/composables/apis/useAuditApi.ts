import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import { withInstantWindow } from '../../utils/withInstantWindow'
import type { AuditActionChoice, AuditApi, AuditEvent } from '../../types/audit'
import type { ListRequest, Paged } from '../../types/list'

/** The endpoint the append-only audit journal is read through. Administrators only. */
const AUDIT_PATH = '/api/v1/audit'

/** The list endpoint's parameter for each filter the audit page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  action: 'action',
  outcome: 'outcome',
  from: 'from',
  to: 'to',
}

/**
 * The audit journal's HTTP surface. Called from a store, never from a component
 * (rules/vue.md: "useApi and the apis composables are called from stores only").
 * @returns The audit endpoints, bound to the shared request pipeline.
 */
export const useAuditApi = (): AuditApi => {
  const api = useApi()

  /**
   * Reads one page of the journal; the panel searches, filters, sorts and counts.
   * @param request The page, size, search, sort and the action, outcome, from and to filters.
   * @param signal Abort signal, so a page left before the response arrives cancels the request.
   * @returns The page.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<AuditEvent>> => {
    return api.get<Paged<AuditEvent>>(`${AUDIT_PATH}${listQuery(withInstantWindow(request), LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Lists the distinct actions the journal holds, for the action filter.
   * @param signal Optional abort signal.
   * @returns The actions with their backend-localized names.
   */
  const actions = (signal?: AbortSignal): Promise<AuditActionChoice[]> => {
    return api.get<AuditActionChoice[]>(`${AUDIT_PATH}/actions`, signal)
  }

  return { list, actions }
}
