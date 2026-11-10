import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  DiscoveredAccount,
  DiscoveredAccountDetail,
  DiscoveryResult,
  RemovedRemoteServer,
  RemoteServer,
  RemoteServerDetail,
  RemoteServerRequest,
  RemoteServersApi,
} from '../../types/migrationSource'

/** The endpoint remote servers (migration sources) are listed and managed through (administrator-only). */
const REMOTE_SERVERS_PATH = '/api/v1/remote-servers'

/** The remote-server list endpoint's parameter for each filter the list page reads from its URL. */
const SERVER_FILTER_PARAMS: Readonly<Record<string, string>> = { state: 'state' }

/** The discovered-accounts list endpoint's parameter for each filter its table reads from its URL. */
const ACCOUNT_FILTER_PARAMS: Readonly<Record<string, string>> = { has: 'has' }

/**
 * Builds the remote-servers API on top of the shared low-level client.
 * @returns The {@link RemoteServersApi} bound to the panel's remote-server endpoints.
 */
export const useRemoteServersApi = (): RemoteServersApi => {
  const api = useApi()

  /**
   * Lists one page of remote servers.
   * @param request The page, size, search, sort and the `state` filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<RemoteServer>> => {
    return api.get<Paged<RemoteServer>>(`${REMOTE_SERVERS_PATH}${listQuery(request, SERVER_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads one remote server.
   * @param id The server's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The server with its detail.
   */
  const get = (id: string, signal?: AbortSignal): Promise<RemoteServerDetail> => {
    return api.get<RemoteServerDetail>(`${REMOTE_SERVERS_PATH}/${id}`, signal)
  }

  /**
   * Adds a remote server.
   * @param request The server's name, address, port, login and optional address limit.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The server as stored.
   */
  const add = (request: RemoteServerRequest, signal?: AbortSignal): Promise<RemoteServerDetail> => {
    return api.post<RemoteServerDetail>(REMOTE_SERVERS_PATH, request, signal)
  }

  /**
   * Changes a remote server.
   * @param id The server's identity.
   * @param request The new values.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The server as it now stands.
   */
  const edit = (id: string, request: RemoteServerRequest, signal?: AbortSignal): Promise<RemoteServerDetail> => {
    return api.put<RemoteServerDetail>(`${REMOTE_SERVERS_PATH}/${id}`, request, signal)
  }

  /**
   * Scans the host keys again.
   * @param id The server's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The server with its new candidates.
   */
  const rescanHostKeys = (id: string, signal?: AbortSignal): Promise<RemoteServerDetail> => {
    return api.post<RemoteServerDetail>(`${REMOTE_SERVERS_PATH}/${id}/host-keys/scan`, undefined, signal)
  }

  /**
   * Pins a scanned host key.
   * @param id The server's identity.
   * @param fingerprint The exact fingerprint of one candidate.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The server with the key pinned.
   */
  const confirmHostKey = (id: string, fingerprint: string, signal?: AbortSignal): Promise<RemoteServerDetail> => {
    return api.post<RemoteServerDetail>(`${REMOTE_SERVERS_PATH}/${id}/host-key`, { fingerprint }, signal)
  }

  /**
   * Tests the connection.
   * @param id The server's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The server with the outcome recorded.
   */
  const test = (id: string, signal?: AbortSignal): Promise<RemoteServerDetail> => {
    return api.post<RemoteServerDetail>(`${REMOTE_SERVERS_PATH}/${id}/test`, undefined, signal)
  }

  /**
   * Removes a remote server.
   * @param id The server's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The public key to take off the source.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<RemovedRemoteServer> => {
    return api.delete<RemovedRemoteServer>(`${REMOTE_SERVERS_PATH}/${id}`, signal)
  }

  /**
   * Runs discovery.
   * @param id The server's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The count and warnings of the run.
   */
  const discover = (id: string, signal?: AbortSignal): Promise<DiscoveryResult> => {
    return api.post<DiscoveryResult>(`${REMOTE_SERVERS_PATH}/${id}/discover`, undefined, signal)
  }

  /**
   * Lists one page of the accounts discovered on a server.
   * @param id The server's identity.
   * @param request The page, size, search, sort and the `has` filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const listAccounts = (id: string, request: ListRequest, signal?: AbortSignal): Promise<Paged<DiscoveredAccount>> => {
    return api.get<Paged<DiscoveredAccount>>(
      `${REMOTE_SERVERS_PATH}/${id}/accounts${listQuery(request, ACCOUNT_FILTER_PARAMS)}`,
      signal,
    )
  }

  /**
   * Reads one discovered account.
   * @param id The server's identity.
   * @param accountId The discovered account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account with its sites and databases.
   */
  const getAccount = (id: string, accountId: string, signal?: AbortSignal): Promise<DiscoveredAccountDetail> => {
    return api.get<DiscoveredAccountDetail>(`${REMOTE_SERVERS_PATH}/${id}/accounts/${accountId}`, signal)
  }

  return {
    list,
    get,
    add,
    edit,
    rescanHostKeys,
    confirmHostKey,
    test,
    remove,
    discover,
    listAccounts,
    getAccount,
  }
}
