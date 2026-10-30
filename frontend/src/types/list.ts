/**
 * What a server-paged list asks the panel for: one page, narrowed by a search, typed filters and a
 * sort. Sent as query parameters on the list request; the server validates every field and refuses
 * a value outside its rules with a 400, so the SPA never has to pre-clamp anything.
 */
export interface ListRequest {
  /** The 1-based page asked for. A page past the end is answered with no items and the true total. */
  page: number
  /** Rows per page; one of the sizes the panel accepts (10, 25, 50 or 100). */
  pageSize: number
  /** Free-text search; empty for none. */
  search: string
  /** A whitelisted sort key, `-` prefixed for descending (`-createdAt`); empty for the server's default. */
  sort: string
  /**
   * Typed filters by their query name (`account`, `status`). Only filters that are set appear here:
   * an absent key means "not narrowed", never an empty string sent to the server.
   */
  filters: Record<string, string>
}

/**
 * One page of a server-paged list, mirroring the panel's `PagedResult(items, total, page, pageSize)`.
 * @template T The row type.
 */
export interface Paged<T> {
  /** The rows on this page, in the order the server sorted them. */
  items: T[]
  /** How many rows match the search and filters across every page. */
  total: number
  /** The page these rows are; echoes the request. */
  page: number
  /** The page size these rows were cut with; echoes the request. */
  pageSize: number
}

/**
 * How one page load ended, as the page's store reports it to the list: the page, the server's
 * refusal with its already-localized reason, or no answer at all. A union rather than a nullable
 * page, so a failure cannot be mistaken for an empty answer; and the two failures are two kinds,
 * the same split every other read in the SPA makes, so the store holds no copy of the
 * "unreachable" text and the page translates that kind itself.
 * @template T The row type.
 */
export type ListLoad<T> =
  | {
      /** The page arrived. */
      kind: 'loaded'
      /** The page the server answered. */
      page: Paged<T>
    }
  | {
      /** The panel answered and refused the request. */
      kind: 'refused'
      /** The panel's reason, already localized. */
      message: string
    }
  | {
      /** No answer arrived; the page shows its own "unreachable" text. */
      kind: 'unreachable'
    }

/**
 * One applied filter of a list, shown by `UiActiveFilter` as a removable chip. It lives here rather
 * than in the component because the list pages and `useListLabels` build it too.
 */
export interface ActiveFilterChip {
  /** The filter's name, emitted with `remove`. */
  key: string
  /** What the filter narrows to, already translated ("Status: active"). */
  text: string
  /** The chip's accessible name, already translated ("Remove filter: Status: active"). */
  removeLabel: string
}
