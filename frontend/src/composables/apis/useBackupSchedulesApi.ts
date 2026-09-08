import { useApi } from '../useApi'
import type {
  BackupSchedule,
  BackupSchedulesApi,
  SaveBackupScheduleRequest,
} from '../../types/backupSchedule'

/** The single endpoint a schedule is read from and written to; the scope travels in the query. */
const BACKUP_SCHEDULES_PATH = '/api/v1/backup-schedules'

/**
 * Builds the backup-schedules API on top of the shared low-level client.
 *
 * Two calls, matching the two routes `BackupSchedulesController` publishes. There is deliberately
 * no delete and no "run now" — see {@link BackupSchedulesApi} for why the backend publishes
 * neither.
 * @returns The {@link BackupSchedulesApi} bound to the panel's schedule endpoints.
 */
export const useBackupSchedulesApi = (): BackupSchedulesApi => {
  const api = useApi()

  /**
   * Reads one schedule. A scope that has never been configured answers 404, which the store turns
   * into a display state rather than an error.
   * @param accountId The account whose override to read, or `null` for the host-wide policy.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The schedule as the panel holds it.
   */
  const get = (accountId: string | null, signal?: AbortSignal): Promise<BackupSchedule> => {
    // The host-wide policy is the request with NO `accountId` at all, not one with an empty value:
    // the query binds `Guid?`, and an empty string is a bad request rather than "no account".
    const path =
      accountId === null
        ? BACKUP_SCHEDULES_PATH
        : `${BACKUP_SCHEDULES_PATH}?accountId=${encodeURIComponent(accountId)}`
    return api.get<BackupSchedule>(path, signal)
  }

  /**
   * Creates or replaces one schedule.
   *
   * The scope is in the BODY here and in the query string on the read, because that is what the
   * server binds: the command carries `accountId`, and a `PUT` to a scoped path would be a second
   * statement of the same fact that could disagree with the first.
   * @param request The schedule to store.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The stored schedule.
   */
  const save = (
    request: SaveBackupScheduleRequest,
    signal?: AbortSignal,
  ): Promise<BackupSchedule> => {
    return api.put<BackupSchedule>(BACKUP_SCHEDULES_PATH, request, signal)
  }

  return { get, save }
}
