import { useApi } from '../useApi'
import type {
  DatabaseGrantsApi,
  GrantRepairReport,
  RepairGrantsRequest,
} from '../../types/grantRepair'

/** The endpoint the grant-table census is read from. */
const DATABASE_GRANTS_PATH = '/api/v1/database-grants'

/**
 * Builds the grant-repair API on top of the shared low-level client.
 *
 * Both calls here are administrator-only on the server: the census spans every account on the host,
 * and a refused row carries identifiers the reader does not own. A customer's call answers 403, and
 * the store that owns these calls reads that as "not disclosed to me" rather than as a failure —
 * which is why this seam is its own file and not folded into `useDatabasesApi`, whose every call is
 * a customer's.
 * @returns The {@link DatabaseGrantsApi} bound to the panel's grant-repair endpoints.
 */
export const useDatabaseGrantsApi = (): DatabaseGrantsApi => {
  const api = useApi()

  /**
   * Reads what a repair would change on this host, changing nothing.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The census, with `isReportOnly` true.
   */
  const report = (signal?: AbortSignal): Promise<GrantRepairReport> => {
    return api.get<GrantRepairReport>(DATABASE_GRANTS_PATH, signal)
  }

  /**
   * Performs the repair, sending back the figure the report gave.
   *
   * The server re-classifies the host and answers 409 when the figure no longer matches, so a stale
   * report cannot be acted on — and a caller that skipped the report has no figure to send.
   * @param request The figure the report gave the operator.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The census of what was actually changed.
   */
  const repair = (
    request: RepairGrantsRequest,
    signal?: AbortSignal,
  ): Promise<GrantRepairReport> => {
    return api.post<GrantRepairReport>(`${DATABASE_GRANTS_PATH}/repair`, request, signal)
  }

  return { report, repair }
}
