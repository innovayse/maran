import type { Paged } from '../../src/types/list'

/** How a stub answers a server-paged list's search, sort and filters for one row type. */
export interface PagedRules<T> {
  /**
   * Whether a row matches the search, compared the way the panel does: case-insensitively, as a
   * substring of the columns that endpoint searches.
   */
  matches: (row: T, term: string) => boolean
  /** The sort keys the endpoint whitelists, each reading the value the rows are ordered by. */
  sorts: Readonly<Record<string, (row: T) => string>>
  /** The endpoint's filter parameters by name, each deciding whether a row passes a value. */
  filters: Readonly<Record<string, (row: T, value: string) => boolean>>
}

/** The page size the panel answers with when the request names none. */
const DEFAULT_PAGE_SIZE = 25

/**
 * Answers a list request the way the panel's paging contract does: the filters and the search
 * narrow, the sort orders (ties keep their catalogue order), and the page is cut from the result.
 * A page past the end is answered with no rows and the true total, never clamped, exactly as the
 * panel answers it, so a spec can watch the SPA move to the last page on its own.
 * @param rows Every row the stub knows, in its default order.
 * @param url The request's URL, whose query carries `page`, `pageSize`, `search`, `sort` and filters.
 * @param rules How this endpoint searches, sorts and filters.
 * @returns The page.
 */
export const pagedAnswer = <T>(rows: readonly T[], url: string, rules: PagedRules<T>): Paged<T> => {
  const query = new URL(url).searchParams
  const page = Number(query.get('page') ?? '1')
  const pageSize = Number(query.get('pageSize') ?? String(DEFAULT_PAGE_SIZE))
  const term = (query.get('search') ?? '').toLowerCase()

  let result = rows.filter((row) => {
    return term === '' || rules.matches(row, term)
  })
  for (const [name, passes] of Object.entries(rules.filters)) {
    const value = query.get(name)
    if (value !== null) {
      result = result.filter((row) => {
        return passes(row, value)
      })
    }
  }

  const sort = query.get('sort') ?? ''
  const descending = sort.startsWith('-')
  const read = rules.sorts[descending ? sort.slice(1) : sort]
  if (read !== undefined) {
    result = [...result].sort((left, right) => {
      const order = read(left).localeCompare(read(right))
      return descending ? -order : order
    })
  }

  const start = (page - 1) * pageSize
  return { items: result.slice(start, start + pageSize), total: result.length, page, pageSize }
}
