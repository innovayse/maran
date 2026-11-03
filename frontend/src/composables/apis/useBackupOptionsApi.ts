import { useApi } from '../useApi'
import type { BackupOptions, BackupOptionsApi } from '../../types/backupOptions'

/** The endpoint the backup screens' choice lists are read from. */
const BACKUP_OPTIONS_PATH = '/api/v1/backup-options'

/**
 * Builds the backup options API on top of the shared low-level client.
 * @returns The {@link BackupOptionsApi} bound to the panel's backup options endpoint.
 */
export const useBackupOptionsApi = (): BackupOptionsApi => {
  const api = useApi()

  /**
   * Reads every closed set the backup screens select from.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The lists, named in the language the request carries.
   */
  const get = (signal?: AbortSignal): Promise<BackupOptions> => {
    return api.get<BackupOptions>(BACKUP_OPTIONS_PATH, signal)
  }

  return { get }
}
