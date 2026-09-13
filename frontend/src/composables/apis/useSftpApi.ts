import { useApi } from '../useApi'
import type {
  CreateSftpUserRequest,
  CreatedSftpUser,
  SftpApi,
  SftpUser,
  SftpUserPassword,
} from '../../types/sftpUser'

/** The endpoint SFTP logins are listed and created through. */
const SFTP_USERS_PATH = '/api/v1/sftp-users'

/**
 * Builds the SFTP users API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 *
 * Two of the four calls answer with a password, and both are named for it — `create` and
 * `resetPassword`. Nothing else in this file returns a credential, and nothing else may be made to:
 * the value exists for one response and is never stored anywhere it could be read back.
 * @returns The {@link SftpApi} bound to the panel's SFTP endpoints.
 */
export const useSftpApi = (): SftpApi => {
  const api = useApi()

  /**
   * Lists the SFTP logins the caller may see. Another customer's rows are not in the answer at
   * all — the server scopes the query, so there is nothing here to filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The logins, in the order the panel reports them.
   */
  const list = (signal?: AbortSignal): Promise<SftpUser[]> => {
    return api.get<SftpUser[]>(SFTP_USERS_PATH, signal)
  }

  /**
   * Creates an SFTP login.
   * @param request The owning account and the name the customer chose.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login as created, including the password shown once.
   */
  const create = (request: CreateSftpUserRequest, signal?: AbortSignal): Promise<CreatedSftpUser> => {
    return api.post<CreatedSftpUser>(SFTP_USERS_PATH, request, signal)
  }

  /**
   * Gives the login a new password. A login belonging to somebody else answers 404, never 403.
   * @param id The login to re-credential.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login and its new password, shown once.
   */
  const resetPassword = (id: string, signal?: AbortSignal): Promise<SftpUserPassword> => {
    return api.post<SftpUserPassword>(`${SFTP_USERS_PATH}/${id}/password`, undefined, signal)
  }

  /**
   * Removes the login. The account's files stay exactly where they are.
   * @param id The login to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed the login.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${SFTP_USERS_PATH}/${id}`, signal)
  }

  return { list, create, resetPassword, remove }
}
