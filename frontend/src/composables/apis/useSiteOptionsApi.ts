import { useApi } from '../useApi'
import type { ReplaceSiteOptionsRequest, SiteOptions, SiteOptionsApi } from '../../types/siteOptions'

/** The collection a site's options live under; the site's id and `options` follow it. */
const SITES_PATH = '/api/v1/sites'

/**
 * Builds the site options API on top of the shared low-level client.
 * @returns The {@link SiteOptionsApi} bound to the panel's site options endpoints.
 */
export const useSiteOptionsApi = (): SiteOptionsApi => {
  const api = useApi()

  /**
   * Reads one site's options with the bounds and choices their screen needs.
   * @param siteId The site to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The options.
   */
  const get = (siteId: string, signal?: AbortSignal): Promise<SiteOptions> => {
    return api.get<SiteOptions>(`${SITES_PATH}/${encodeURIComponent(siteId)}/options`, signal)
  }

  /**
   * Replaces all of a site's options. Idempotent: saving the same set again converges to the same state.
   * @param siteId The site to change.
   * @param request The complete new set.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The options as the panel stored them.
   */
  const replace = (siteId: string, request: ReplaceSiteOptionsRequest, signal?: AbortSignal): Promise<SiteOptions> => {
    return api.put<SiteOptions>(`${SITES_PATH}/${encodeURIComponent(siteId)}/options`, request, signal)
  }

  return { get, replace }
}
