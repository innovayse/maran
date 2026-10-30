import type { ListRequest } from '../types/list'

/**
 * Builds the query string a server-paged list request carries. The page and the page size are
 * always sent; the search, the sort and each filter only when set, because the panel binds an
 * empty parameter as a missing value and refuses it rather than reading it as "none".
 *
 * A filter is sent under the name the endpoint binds, which is not always the name the SPA's URL
 * uses: the list page reads `?account=` and the endpoint takes `accountId`. The map is the
 * endpoint's, so it lives with the API composable that calls it. A filter the map does not name is
 * not sent: only parameters the endpoint declares ever leave the browser.
 * @param request The page, size, search, sort and filters.
 * @param filterParams The endpoint's parameter name for each filter the list takes, by filter name.
 * @returns The query string including its leading `?`.
 */
export const listQuery = (request: ListRequest, filterParams: Readonly<Partial<Record<string, string>>>): string => {
  const params = new URLSearchParams({ page: String(request.page), pageSize: String(request.pageSize) })
  if (request.search !== '') {
    params.set('search', request.search)
  }
  if (request.sort !== '') {
    params.set('sort', request.sort)
  }
  for (const [key, value] of Object.entries(request.filters)) {
    const name = filterParams[key]
    if (name !== undefined && value !== '') {
      params.set(name, value)
    }
  }
  return `?${params.toString()}`
}
