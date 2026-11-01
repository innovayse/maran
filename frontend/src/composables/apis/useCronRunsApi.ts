import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type { CronRun, CronRunSummary, CronRunsApi } from '../../types/cronRun'

/** The filters the history's URL carries, by the name the panel's query reads them under. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  outcome: 'outcome',
}

/** The endpoint an entry's runs hang under: `${CRON_ENTRIES_PATH}/{entryId}/runs`. */
const CRON_ENTRIES_PATH = '/api/v1/cron-entries'

/**
 * Builds the run-now and run-history API on top of the shared low-level client (issue #158).
 *
 * Its own composable rather than more calls on `useCronApi`, because it is its own controller and its
 * own store: the history is a server-paged list and the entries are not.
 * @returns The {@link CronRunsApi} bound to the panel's run endpoints.
 */
export const useCronRunsApi = (): CronRunsApi => {
  const api = useApi()

  /**
   * Runs the entry now and waits for it; the request lasts as long as the job, up to two minutes.
   * @param entryId The entry to run.
   * @param accountId The account whose crontab holds it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The recorded run.
   */
  const run = (entryId: string, accountId: string, signal?: AbortSignal): Promise<CronRun> => {
    return api.post<CronRun>(`${CRON_ENTRIES_PATH}/${encodeURIComponent(entryId)}/runs`, { accountId }, signal)
  }

  /**
   * Lists one page of the entry's runs; the account rides beside the list's own parameters.
   * @param entryId The entry whose runs to list.
   * @param accountId The account whose crontab holds it.
   * @param request The page, size, search, sort and outcome filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (
    entryId: string,
    accountId: string,
    request: ListRequest,
    signal?: AbortSignal,
  ): Promise<Paged<CronRunSummary>> => {
    const query = `${listQuery(request, LIST_FILTER_PARAMS)}&${new URLSearchParams({ accountId }).toString()}`
    return api.get<Paged<CronRunSummary>>(`${CRON_ENTRIES_PATH}/${encodeURIComponent(entryId)}/runs${query}`, signal)
  }

  /**
   * Reads one run with its output.
   * @param entryId The entry the run belongs to.
   * @param accountId The account whose crontab holds it.
   * @param runId The run.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The run.
   */
  const get = (entryId: string, accountId: string, runId: string, signal?: AbortSignal): Promise<CronRun> => {
    const query = new URLSearchParams({ accountId }).toString()
    return api.get<CronRun>(`${CRON_ENTRIES_PATH}/${encodeURIComponent(entryId)}/runs/${encodeURIComponent(runId)}?${query}`, signal)
  }

  return { run, list, get }
}
