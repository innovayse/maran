import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  BackupDestination,
  BackupDestinationsApi,
  SetBackupDestinationEncryptionRequest,
} from '../../types/backupDestination'

/** The route the recorded destinations are read from. */
const BACKUP_DESTINATIONS_PATH = '/api/v1/backup-destinations'

/** The list endpoint's parameter for each filter the destinations page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  kind: 'kind',
}

/**
 * Builds the backup-destinations API on top of the shared low-level client.
 *
 * Two calls: the read, and the encryption switch (issue #160). See {@link BackupDestinationsApi} for why
 * the controller's `POST` is deliberately not wrapped here.
 * @returns The {@link BackupDestinationsApi} bound to the panel's destinations endpoint.
 */
export const useBackupDestinationsApi = (): BackupDestinationsApi => {
  const api = useApi()

  /**
   * Reads one page of the destinations, searched, sorted and filtered by the panel. The order is
   * not re-imposed here: the server states it, and a second sort in the SPA would be a second
   * authority that can disagree with the first.
   * @param request The page, size, search on the name, sort and the kind filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<BackupDestination>> => {
    return api.get<Paged<BackupDestination>>(`${BACKUP_DESTINATIONS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Switches encryption of new backups on or off for one destination. The answer is 204, so there
   * is nothing to parse.
   * @param id The destination.
   * @param enabled Whether new backups written there are sealed.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel recorded the change.
   */
  const setEncryption = async (id: string, enabled: boolean, signal?: AbortSignal): Promise<void> => {
    const body: SetBackupDestinationEncryptionRequest = { enabled }
    await api.put<void>(`${BACKUP_DESTINATIONS_PATH}/${id}/encryption`, body, signal)
  }

  return { list, setEncryption }
}
