import { useApi } from '../useApi'
import type { InstallLicenceRequest, LicenceApi, LicenceStatus } from '../../types/licence'

/** The endpoint the licence status is read from. */
const LICENCE_STATUS_PATH = '/api/v1/licence-status'

/** The endpoint a pasted licence is installed through. */
const LICENCE_INSTALL_PATH = '/api/v1/licence-status/install'

/**
 * Builds the licence API on top of the shared low-level client.
 * @returns The {@link LicenceApi} bound to the licence-status endpoints.
 */
export const useLicenceApi = (): LicenceApi => {
  const api = useApi()

  /**
   * Reads the licence status.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The panel's licence status.
   */
  const status = (signal?: AbortSignal): Promise<LicenceStatus> => {
    return api.get<LicenceStatus>(LICENCE_STATUS_PATH, signal)
  }

  /**
   * Installs a pasted licence.
   * @param rawLicenceText The licence text exactly as pasted.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The licence status after the install.
   */
  const install = (rawLicenceText: string, signal?: AbortSignal): Promise<LicenceStatus> => {
    const body: InstallLicenceRequest = { rawLicenceText }
    return api.post<LicenceStatus>(LICENCE_INSTALL_PATH, body, signal)
  }

  return { status, install }
}
