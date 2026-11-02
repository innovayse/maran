import { useApi } from '../useApi'
import type { Favourites, FavouritesApi, PinPageRequest } from '../../types/favourite'

/** The endpoint the caller's own pinned pages live at. */
const PINNED_PAGES_PATH = '/api/v1/me/pinned-pages'

/**
 * Builds the favourites API on top of the shared low-level client. The endpoint is scoped to the
 * caller's own token, so no call here names a user.
 * @returns The {@link FavouritesApi} bound to the pinned-pages endpoint.
 */
export const useFavouritesApi = (): FavouritesApi => {
  const api = useApi()

  /**
   * Reads the caller's pins.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The pins and the limit.
   */
  const list = (signal?: AbortSignal): Promise<Favourites> => {
    return api.get<Favourites>(PINNED_PAGES_PATH, signal)
  }

  /**
   * Pins a page.
   * @param request The key of the page.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The pins after the change.
   */
  const pin = (request: PinPageRequest, signal?: AbortSignal): Promise<Favourites> => {
    return api.post<Favourites>(PINNED_PAGES_PATH, request, signal)
  }

  /**
   * Removes a page from the pins.
   * @param itemKey The key of the page.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The pins after the change.
   */
  const unpin = (itemKey: string, signal?: AbortSignal): Promise<Favourites> => {
    return api.delete<Favourites>(`${PINNED_PAGES_PATH}/${encodeURIComponent(itemKey)}`, signal)
  }

  return { list, pin, unpin }
}
