import { useApi } from '../useApi'
import type { BackupDestination, BackupDestinationsApi } from '../../types/backupDestination'

/** The route the recorded destinations are read from; it takes no parameters. */
const BACKUP_DESTINATIONS_PATH = '/api/v1/backup-destinations'

/**
 * Builds the backup-destinations API on top of the shared low-level client.
 *
 * One call, matching the one route this panel can act on. See {@link BackupDestinationsApi} for why
 * the controller's `POST` is deliberately not wrapped here.
 * @returns The {@link BackupDestinationsApi} bound to the panel's destinations endpoint.
 */
export const useBackupDestinationsApi = (): BackupDestinationsApi => {
  const api = useApi()

  /**
   * Reads every destination, in the order the panel returns them: the default first, then by name.
   *
   * The order is not re-imposed here. The server states it, and a second sort in the SPA would be a
   * second authority for it that can disagree with the first.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The destinations as the panel holds them.
   */
  const list = (signal?: AbortSignal): Promise<BackupDestination[]> => {
    return api.get<BackupDestination[]>(BACKUP_DESTINATIONS_PATH, signal)
  }

  return { list }
}
