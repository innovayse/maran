import { useApi } from '../useApi'
import type {
  BackupSchedule,
  BackupScheduleScopeView,
  MyBackupScheduleApi,
  SaveMyBackupScheduleRequest,
} from '../../types/backupSchedule'

/** The single endpoint the customer's own schedule is read from and written to; it names no account. */
const MY_BACKUP_SCHEDULE_PATH = '/api/v1/me/backup-schedule'

/**
 * Builds the customer's own backup-schedule API on top of the shared low-level client.
 *
 * Two calls, matching the two routes `MyBackupScheduleController` publishes. Neither carries an
 * account: the server takes it from the signed-in login, so there is nothing here that could aim a
 * request at somebody else's schedule.
 * @returns The {@link MyBackupScheduleApi} bound to the customer's schedule endpoint.
 */
export const useMyBackupScheduleApi = (): MyBackupScheduleApi => {
  const api = useApi()

  /**
   * Reads the caller's own schedule.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The scope, holding the schedule or none.
   */
  const get = (signal?: AbortSignal): Promise<BackupScheduleScopeView> => {
    return api.get<BackupScheduleScopeView>(MY_BACKUP_SCHEDULE_PATH, signal)
  }

  /**
   * Creates or replaces the caller's own schedule.
   * @param request The schedule to store.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The stored schedule.
   */
  const save = (request: SaveMyBackupScheduleRequest, signal?: AbortSignal): Promise<BackupSchedule> => {
    return api.put<BackupSchedule>(MY_BACKUP_SCHEDULE_PATH, request, signal)
  }

  return { get, save }
}
