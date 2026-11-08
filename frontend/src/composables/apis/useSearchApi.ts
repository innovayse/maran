import { useApi } from '../useApi'
import type { SearchApi, SearchResult } from '../../types/search'

/** The endpoint the palette's server-side groups are read from. */
const SEARCH_PATH = '/api/v1/search'

/**
 * Builds the global-search API on top of the shared low-level client.
 * @returns The {@link SearchApi} bound to the panel's search endpoint.
 */
export const useSearchApi = (): SearchApi => {
  const api = useApi()

  /**
   * Asks the panel what matches a term; a POST so the text stays out of every URL.
   * @param term The text typed.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The groups the panel answered with.
   */
  const search = (term: string, signal?: AbortSignal): Promise<SearchResult> => {
    return api.post<SearchResult>(SEARCH_PATH, { term }, signal)
  }

  return { search }
}
