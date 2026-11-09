import { useApi } from '../useApi'
import type { BrandingApi, ResellerBranding, SaveResellerBrandingRequest } from '../../types/branding'

/** The reseller's own branding: read it, replace it. */
const OWN_PATH = '/api/v1/reseller/branding'

/** The branding the caller's shell shows, whoever the caller is. */
const SHELL_PATH = '/api/v1/accounts/me/branding'

/**
 * Builds the branding API on top of the shared low-level client.
 * @returns The {@link BrandingApi} bound to the panel's branding endpoints.
 */
export const useBrandingApi = (): BrandingApi => {
  const api = useApi()

  /**
   * Reads the branding of the reseller the caller is.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The stored branding, every member `null` when none is set.
   */
  const getOwn = (signal?: AbortSignal): Promise<ResellerBranding> => {
    return api.get<ResellerBranding>(OWN_PATH, signal)
  }

  /**
   * Replaces the reseller's branding.
   * @param request All three values; one sent as `null` goes back to the panel's own.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The branding as the panel stored it.
   */
  const save = (request: SaveResellerBrandingRequest, signal?: AbortSignal): Promise<ResellerBranding> => {
    return api.put<ResellerBranding>(OWN_PATH, request, signal)
  }

  /**
   * Reads the branding the caller's own shell shows.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The branding, every member `null` when the panel's own applies.
   */
  const getShell = (signal?: AbortSignal): Promise<ResellerBranding> => {
    return api.get<ResellerBranding>(SHELL_PATH, signal)
  }

  return { getOwn, save, getShell }
}
