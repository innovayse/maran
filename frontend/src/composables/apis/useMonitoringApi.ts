import { useApi } from '../useApi'
import type { AccountDiskUsage, ChartRange, MetricsChart, MonitoringApi, ServiceStatus } from '../../types/monitoring'

/** The endpoint the bucketed samples behind the charts are read from. */
const CHART_PATH = '/api/v1/monitoring/chart'

/** The endpoint the watched services' states are read from. */
const SERVICES_PATH = '/api/v1/monitoring/services'

/** The endpoint the per-account disk figures are read from. */
const ACCOUNTS_DISK_PATH = '/api/v1/monitoring/accounts-disk'

/**
 * Builds the monitoring API on top of the shared low-level client.
 *
 * Every call here is a read: the monitoring module exposes no mutation at all, which is why this
 * file has no request bodies and no `post`. The screen's only parameter is the chart range, and it
 * travels in the query string because that is where `[FromQuery] ChartRange range` binds it from.
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
   * Reads what every hosting account occupies on disk beside what its plan allows.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per account the panel holds.
   */
  const listAccountDiskUsage = (signal?: AbortSignal): Promise<AccountDiskUsage[]> => {
    return api.get<AccountDiskUsage[]>(ACCOUNTS_DISK_PATH, signal)
  }

  return { getChart, listServices, listAccountDiskUsage }
}
