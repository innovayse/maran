import { useApi } from '../useApi'
import type {
  CreateFtpUserRequest,
  CreatedFtpUser,
  FtpUser,
  FtpUserPassword,
  FtpUsersApi,
} from '../../types/ftpUser'

/** The endpoint FTPS logins are listed and created through. */
const FTP_USERS_PATH = '/api/v1/ftp-users'

/**
 * Builds the FTPS users API on top of the shared low-level client.
 *
 * One composable per backend feature, so the customer's login endpoints and the administrator's
 * server switch (`useFtpsServerApi`) are two files rather than one: they answer to two different
 * authorization policies, and a single composable would invite a screen to call the admin-only half
 * on a customer's behalf.
 * @returns The {@link FtpUsersApi} bound to the panel's FTPS login endpoints.
 */
export const useFtpUsersApi = (): FtpUsersApi => {
  const api = useApi()

  /**
   * Lists the FTPS logins the caller may see. Another customer's rows are not in the answer at all —
   * the server scopes the query, so there is nothing here to filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The logins, in the order the panel reports them.
   */
  const list = (signal?: AbortSignal): Promise<FtpUser[]> => {
    return api.get<FtpUser[]>(FTP_USERS_PATH, signal)
  }

  /**
   * Creates an FTPS login.
   * @param request The owning account and the name the customer chose.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login as created, including the password shown once.
   */
  const create = (request: CreateFtpUserRequest, signal?: AbortSignal): Promise<CreatedFtpUser> => {
    return api.post<CreatedFtpUser>(FTP_USERS_PATH, request, signal)
  }

  /**
   * Gives the login a new password. A login belonging to somebody else answers 404, never 403.
   * @param id The login to re-credential.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login and its new password, shown once.
   */
  const resetPassword = (id: string, signal?: AbortSignal): Promise<FtpUserPassword> => {
    return api.post<FtpUserPassword>(`${FTP_USERS_PATH}/${id}/password`, undefined, signal)
  }

  /**
   * Removes the login. The account's files stay exactly where they are.
   * @param id The login to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed the login.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${FTP_USERS_PATH}/${id}`, signal)
  }

  return { list, create, resetPassword, remove }
}
