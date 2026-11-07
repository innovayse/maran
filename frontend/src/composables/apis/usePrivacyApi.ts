import { useApi } from '../useApi'
import type { ApiDownload } from '../../types/api'
import type { PrivacyApi } from '../../types/privacy'

/** The endpoint the personal-data exports are read through. Administrators only. */
const PRIVACY_PATH = '/api/v1/privacy'

/**
 * The privacy exports' HTTP surface. Called from a store, never from a component
 * (rules/vue.md: "useApi and the apis composables are called from stores only").
 * @returns The privacy endpoints, bound to the shared request pipeline.
 */
export const usePrivacyApi = (): PrivacyApi => {
  const api = useApi()

  /**
   * Downloads everything the panel holds about one account.
   * @param accountId The account to export; encoded into the path.
   * @param signal Abort signal, so leaving the screen cancels a download in flight.
   * @returns The file and its proposed name.
   */
  const exportAccount = (accountId: string, signal?: AbortSignal): Promise<ApiDownload> => {
    return api.download(`${PRIVACY_PATH}/accounts/${encodeURIComponent(accountId)}/export`, signal)
  }

  return { exportAccount }
}
