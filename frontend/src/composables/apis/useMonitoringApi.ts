import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type { AccountDiskUsage, ChartRange, MetricsChart, MonitoringApi, ServiceStatus } from '../../types/monitoring'

/** The endpoint the bucketed samples behind the charts are read from. */
const CHART_PATH = '/api/v1/monitoring/chart'

/** The endpoint the watched services' states are read from. */
const SERVICES_PATH = '/api/v1/monitoring/services'

/** The endpoint the per-account disk figures are read from. */
const ACCOUNTS_DISK_PATH = '/api/v1/monitoring/accounts-disk'

/** The per-account disk list takes no typed filter: its only narrowing is the search. */
const ACCOUNTS_DISK_FILTER_PARAMS: Readonly<Record<string, string>> = {}

/**
 * Builds the monitoring API on top of the shared low-level client.
 *
 * Every call here is a read: the monitoring module exposes no mutation at all, which is why this
 * file has no request bodies and no `post`. The chart range and the disk list's page travel in the
 * query string, because that is where the endpoints bind them from.
 * @returns The {@link MonitoringApi} bound to the panel's monitoring endpoints.
 */
export const useMonitoringApi = (): MonitoringApi => {
  const api = useApi()

  /**
   * Reads the stored samples for a range, already bucketed by the panel.
   *
   * `URLSearchParams` rather than a template literal, for the same reason every other query string
   * in this SPA is built that way: the encoding is the client's job, not the reader's to verify.
   * @param range How far back the chart reaches.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The chart, with its range echoed back.
   */
  const getChart = (range: ChartRange, signal?: AbortSignal): Promise<MetricsChart> => {
    const query = new URLSearchParams({ range }).toString()
    return api.get<MetricsChart>(`${CHART_PATH}?${query}`, signal)
  }

  /**
   * Reads whether each service the agent watches is up, down, or not known.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per watched service.
   */
  const listServices = (signal?: AbortSignal): Promise<ServiceStatus[]> => {
    return api.get<ServiceStatus[]>(SERVICES_PATH, signal)
  }

  /**
   * Reads one page of what the hosting accounts occupy on disk, searched and sorted by the panel,
   * which measures only the accounts on that page.
   * @param request The page, size, search on the user name and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const listAccountDiskUsage = (request: ListRequest, signal?: AbortSignal): Promise<Paged<AccountDiskUsage>> => {
    return api.get<Paged<AccountDiskUsage>>(
      `${ACCOUNTS_DISK_PATH}${listQuery(request, ACCOUNTS_DISK_FILTER_PARAMS)}`,
      signal,
    )
  }

  return { getChart, listServices, listAccountDiskUsage }
}
