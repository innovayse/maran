import { useApi } from '../useApi'
import type {
  Backup,
  BackupsApi,
  CreateBackupRequest,
  RestoreBackupRequest,
  RestoreOutcome,
} from '../../types/backup'

/** The collection endpoint backups are listed and created through. */
const BACKUPS_PATH = '/api/v1/backups'

/**
 * Builds the backups API on top of the shared low-level client.
 *
 * Five calls, matching the five routes `BackupsController` publishes, and no sixth. In particular
 * there is **no download**: the panel deliberately exposes no endpoint that returns an archive's
 * bytes, because an archive holds an account's files and the contents of its databases, and an
 * operator retrieves one from the server where the file already is.
 * @returns The {@link BackupsApi} bound to the panel's backup endpoints.
 */
export const useBackupsApi = (): BackupsApi => {
  const api = useApi()

  /**
   * Lists the backups the caller may see. Another customer's rows are not in the answer at all —
   * the server scopes the query, so there is nothing here to filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The backups, in the order the panel reports them.
   */
  const list = (signal?: AbortSignal): Promise<Backup[]> => {
    return api.get<Backup[]>(BACKUPS_PATH, signal)
  }

  /**
   * Reads one backup. Another customer's backup answers 404, never 403.
   * @param id The backup to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The backup as the panel holds it now.
   */
  const get = (id: string, signal?: AbortSignal): Promise<Backup> => {
    return api.get<Backup>(`${BACKUPS_PATH}/${id}`, signal)
  }

  /**
   * Takes a backup of an account now.
   *
   * The request does not return until the run has finished, which can be minutes for a large
   * account: the panel's create endpoint is synchronous and answers with the finished record.
   * @param request The account to back up.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The finished record — completed or failed, which its status says.
   */
  const create = (request: CreateBackupRequest, signal?: AbortSignal): Promise<Backup> => {
    return api.post<Backup>(BACKUPS_PATH, request, signal)
  }

  /**
   * Deletes a backup's archive and its record.
   * @param id The backup to delete.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel deleted it.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${BACKUPS_PATH}/${id}`, signal)
  }

  /**
   * Replaces an account from one of its backups.
   *
   * The body carries the typed confirmation and nothing else. The backup id travels in the PATH
   * because the server binds it from the route and refuses to bind it from the body: a body-bound
   * id would let a caller confirm one account's name and have a different account's backup
   * restored, which is the whole confirmation defeated in one field.
   *
   * Like create, this does not answer until the run has finished; unlike create, a run that ended
   * badly is a rejection rather than a record, because 200 is returned only for a whole restore.
   * @param id The backup to restore from.
   * @param request The typed confirmation.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns What the restore replaced.
   */
  const restore = (
    id: string,
    request: RestoreBackupRequest,
    signal?: AbortSignal,
  ): Promise<RestoreOutcome> => {
    return api.post<RestoreOutcome>(`${BACKUPS_PATH}/${id}/restore`, request, signal)
  }

  return { list, get, create, remove, restore }
}
