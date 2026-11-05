import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  AddSftpUserKeyRequest,
  CreateSftpUserRequest,
  CreatedSftpUser,
  SftpApi,
  SftpUser,
  SftpUserKey,
  SftpUserKeysQuery,
  SftpUserPassword,
} from '../../types/sftpUser'

/** The endpoint SFTP logins are listed and created through. */
const SFTP_USERS_PATH = '/api/v1/sftp-users'

/** The list endpoint's parameter for each filter the SFTP logins page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  account: 'accountId',
}

/**
 * Builds the SFTP users API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 *
 * Two of the calls answer with a password, and both are named for it — `create` and
 * `resetPassword`. Nothing else in this file returns a credential, and nothing else may be made to:
 * the value exists for one response and is never stored anywhere it could be read back.
 * @returns The {@link SftpApi} bound to the panel's SFTP endpoints.
 */
export const useSftpApi = (): SftpApi => {
  const api = useApi()

  /**
   * Lists one page of the SFTP logins the caller may see. Another customer's rows are not in the
   * answer at all — the server scopes the query and its total, so there is nothing here to filter.
   * @param request The page, size, search, sort and the account filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<SftpUser>> => {
    return api.get<Paged<SftpUser>>(`${SFTP_USERS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
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
   * @returns Resolves once the panel removed the login; the answer is an empty 204.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${SFTP_USERS_PATH}/${id}`, signal)
  }

  /**
   * Lists one page of a login's public keys, searched by label. A login belonging to somebody else
   * answers 404, never 403.
   * @param loginId The login whose keys are listed.
   * @param query The page, size and label search.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  const listKeys = (loginId: string, query: SftpUserKeysQuery, signal?: AbortSignal): Promise<Paged<SftpUserKey>> => {
    // The keys list has no sort and no filter; the shared builder wants the full request shape.
    const request: ListRequest = { ...query, sort: '', filters: {} }
    return api.get<Paged<SftpUserKey>>(`${SFTP_USERS_PATH}/${loginId}/keys${listQuery(request, {})}`, signal)
  }

  /**
   * Adds a public key to a login. Adding a key never ends an open session.
   * @param loginId The login that gains the key.
   * @param request The label and the key line.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The key as stored.
   */
  const addKey = (loginId: string, request: AddSftpUserKeyRequest, signal?: AbortSignal): Promise<SftpUserKey> => {
    return api.post<SftpUserKey>(`${SFTP_USERS_PATH}/${loginId}/keys`, request, signal)
  }

  /**
   * Removes a public key from a login. The login's open sessions are ended by the panel.
   * @param loginId The login that loses the key.
   * @param keyId The key to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel removed the key; the answer is an empty 204.
   */
  const removeKey = (loginId: string, keyId: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${SFTP_USERS_PATH}/${loginId}/keys/${keyId}`, signal)
  }

  return { list, create, resetPassword, remove, listKeys, addKey, removeKey }
}
