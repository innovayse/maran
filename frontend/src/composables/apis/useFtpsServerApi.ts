import { useApi } from '../useApi'
import type { EnableFtpsRequest, FtpsServerApi, FtpsStatus } from '../../types/ftpsStatus'

/** The endpoint the FTPS daemon's measured status is read from. */
const FTPS_SERVER_PATH = '/api/v1/ftps-server'

/**
 * Builds the FTPS server API on top of the shared low-level client.
 *
 * Every call here is administrator-only on the server: there is one FTPS daemon on the host and
 * switching it affects every account on it. A customer's call answers 403, and the store that owns
 * these calls reads that as "not disclosed to me" rather than as a failure — which is why the seam
 * is its own file and not folded into `useFtpUsersApi`.
 * @returns The {@link FtpsServerApi} bound to the panel's FTPS server endpoints.
 */
export const useFtpsServerApi = (): FtpsServerApi => {
  const api = useApi()

  /**
   * Reads what the FTPS daemon is doing, measured on the host rather than recalled.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The measured status.
   */
  const status = (signal?: AbortSignal): Promise<FtpsStatus> => {
    return api.get<FtpsStatus>(FTPS_SERVER_PATH, signal)
  }

  /**
   * Configures the daemon for a hostname this panel serves and brings it up.
   * @param request The hostname, and the passive address for a host behind NAT.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status measured after the change.
   */
  const enable = (request: EnableFtpsRequest, signal?: AbortSignal): Promise<FtpsStatus> => {
    return api.post<FtpsStatus>(`${FTPS_SERVER_PATH}/enable`, request, signal)
  }

  /**
   * Stops the daemon. Customer logins are left as they are, and so are the firewall rules — the two
   * warnings on the server panel exist because disabling a service is not editing a firewall.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status measured after the change.
   */
  const disable = (signal?: AbortSignal): Promise<FtpsStatus> => {
    return api.post<FtpsStatus>(`${FTPS_SERVER_PATH}/disable`, undefined, signal)
  }

  return { status, enable, disable }
}
