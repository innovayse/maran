import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import { withInstantWindow } from '../../utils/withInstantWindow'
import type { ApiDownload } from '../../types/api'
import type {
  AuditActionChoice,
  AuditApi,
  AuditChainStatus,
  AuditChainVerification,
  AuditEvent,
  AuditExportFormat,
  AuditOutcomeChoice,
  AuditRetention,
} from '../../types/audit'
import type { ListRequest, Paged } from '../../types/list'

/** The endpoint the append-only audit journal is read through. Administrators only. */
const AUDIT_PATH = '/api/v1/audit'

/** The list endpoint's parameter for each filter the audit page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  action: 'action',
  outcome: 'outcome',
  from: 'from',
  to: 'to',
  actor: 'actor',
  account: 'account',
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

  /**
   * Lists the outcomes the journal can be filtered by, for the outcome filter.
   * @param signal Optional abort signal.
   * @returns The outcomes with their backend-localized names.
   */
  const outcomes = (signal?: AbortSignal): Promise<AuditOutcomeChoice[]> => {
    return api.get<AuditOutcomeChoice[]>(`${AUDIT_PATH}/outcomes`, signal)
  }

  /**
   * Downloads the journal as a file. The export takes the list's search and filters but no page or
   * sort: it is the whole matching journal in chronological order, so only parameters the endpoint
   * declares are sent.
   * @param request The search and filters; its page and sort are not sent.
   * @param format The file format asked for.
   * @param signal Abort signal, so leaving the page cancels a download in flight.
   * @returns The file and its proposed name.
   */
  const exportJournal = (
    request: ListRequest,
    format: AuditExportFormat,
    signal?: AbortSignal,
  ): Promise<ApiDownload> => {
    const params = new URLSearchParams({ format })
    if (request.search !== '') {
      params.set('search', request.search)
    }
    for (const [key, value] of Object.entries(withInstantWindow(request).filters)) {
      const name = LIST_FILTER_PARAMS[key]
      if (name !== undefined && value !== '') {
        params.set(name, value)
      }
    }
    return api.download(`${AUDIT_PATH}/export?${params.toString()}`, signal)
  }

  /**
   * Reads how long the journal keeps its entries.
   * @param signal Optional abort signal.
   * @returns The retention setting with the bounds the panel accepts.
   */
  const retention = (signal?: AbortSignal): Promise<AuditRetention> => {
    return api.get<AuditRetention>(`${AUDIT_PATH}/retention`, signal)
  }

  /**
   * Replaces the retention setting.
   * @param retentionDays Days to keep entries, or `null` to keep them indefinitely.
   * @param signal Optional abort signal.
   * @returns Resolves once the panel has stored it.
   */
  const saveRetention = (retentionDays: number | null, signal?: AbortSignal): Promise<void> => {
    return api.put<void>(`${AUDIT_PATH}/retention`, { retentionDays }, signal)
  }

  /**
   * Reads the tamper-evidence chain's state.
   * @param signal Optional abort signal.
   * @returns The sealed and unsealed counts and the purge position.
   */
  const chainStatus = (signal?: AbortSignal): Promise<AuditChainStatus> => {
    return api.get<AuditChainStatus>(`${AUDIT_PATH}/chain`, signal)
  }

  /**
   * Asks the panel to verify the whole chain now. A POST because it does work and is journalled,
   * though it sends no body.
   * @param signal Optional abort signal.
   * @returns The verdict.
   */
  const verifyChain = (signal?: AbortSignal): Promise<AuditChainVerification> => {
    return api.post<AuditChainVerification>(`${AUDIT_PATH}/chain/verification`, undefined, signal)
  }

  return { list, actions, outcomes, exportJournal, retention, saveRetention, chainStatus, verifyChain }
}
