import { computed, onScopeDispose, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useRoute, useRouter, type LocationQuery, type LocationQueryRaw } from 'vue-router'
import type { LoadingState } from '../types/loading'
import type { ListLoad, ListRequest } from '../types/list'

/** How one typed filter of a list is read from the URL. */
export interface ListFilterSpec {
  /**
   * Whether a value found in the URL is honoured. A value it refuses is treated as absent, so a
   * hand-edited or stale link never reaches the server as a filter the page cannot show. Left out,
   * every non-empty value is honoured; the server validates it either way.
   */
  accepts?: (value: string) => boolean
}

/** The typed filters a list takes, by their query-parameter name (`account`, `status`). */
export type ListFilterSchema = Record<string, ListFilterSpec>

/** What a list page hands {@link useServerTable}. */
export interface ServerTableOptions<T> {
  /** The list's typed filters; omitted for a list that has none. */
  filters?: ListFilterSchema
  /**
   * Fetches one page through the page's store and reports how it ended. The composable never calls
   * the API itself (rules/vue.md).
   */
  load: (request: ListRequest) => Promise<ListLoad<T>>
}

/** Where the list's latest fetch stands; the message exists only on a refusal. */
export type ServerTableState =
  | {
      /** A page is being fetched. */
      status: 'loading'
    }
  | {
      /** The latest page arrived. */
      status: 'loaded'
    }
  | {
      /** The panel refused the latest fetch. */
      status: 'refused'
      /** Why, already localized by the server. */
      message: string
    }
  | {
      /** The latest fetch got no answer; the page shows its own "unreachable" text. */
      status: 'unreachable'
    }

/** A server-paged list's state, kept in the URL, and the actions that change it. */
export interface ServerTable {
  /** The request the current URL describes; what was last handed to `load`. */
  request: ComputedRef<ListRequest>
  /** The 1-based current page. */
  page: ComputedRef<number>
  /** Rows per page. */
  pageSize: ComputedRef<number>
  /** The page sizes a reader may choose from. */
  pageSizes: readonly number[]
  /** The search box's text. Bind it with `v-model`; it reaches the URL after a short pause. */
  search: Ref<string>
  /** The current sort key, `-` prefixed when descending; empty for the server's default. */
  sort: ComputedRef<string>
  /** Every filter in the schema with its applied value, empty when unset. */
  filters: ComputedRef<Record<string, string>>
  /** How many filters are set; feeds `UiFilterTrigger`'s dot and count. */
  activeFilterCount: ComputedRef<number>
  /** Rows matching the search and filters across every page; 0 before the first answer. */
  total: Ref<number>
  /** How many pages the total fills; at least 1, so "page 1 of 1" is said of an empty list. */
  pageCount: ComputedRef<number>
  /** Where the latest fetch stands: loading, loaded, refused with the server's message, or unreachable. */
  state: Ref<ServerTableState>
  /**
   * The same state as a `UiLoadingRegion` reads it: a refusal and no answer are both `error`, the
   * kinds the page tells apart in the alert it puts in the region's `error` slot.
   */
  loadingState: ComputedRef<LoadingState>
  /**
   * Moves to a page.
   * @param page The 1-based page.
   * @returns Resolves once the URL has changed.
   */
  setPage: (page: number) => Promise<void>
  /**
   * Changes the page size and goes back to page 1.
   * @param pageSize One of {@link ServerTable.pageSizes}.
   * @returns Resolves once the URL has changed.
   */
  setPageSize: (pageSize: number) => Promise<void>
  /**
   * Sorts by a field: ascending first, descending when it is already the ascending sort, and back.
   * Goes back to page 1.
   * @param field A sort key the server whitelists for this list.
   * @returns Resolves once the URL has changed.
   */
  toggleSort: (field: string) => Promise<void>
  /**
   * Replaces every filter at once (what a filter sheet's Apply does) and goes back to page 1.
   * @param next Values by filter name; an empty value clears that filter.
   * @returns Resolves once the URL has changed.
   */
  setFilters: (next: Record<string, string>) => Promise<void>
  /**
   * Clears one filter (a chip's remove) and goes back to page 1.
   * @param key The filter's name.
   * @returns Resolves once the URL has changed.
   */
  clearFilter: (key: string) => Promise<void>
  /**
   * Clears every filter and goes back to page 1.
   * @returns Resolves once the URL has changed.
   */
  clearFilters: () => Promise<void>
  /**
   * Fetches the current page again, e.g. after a row was created or deleted.
   * @returns Resolves once the page settled, successfully or not.
   */
  reload: () => Promise<void>
}

/** The page size a list opens with. */
const DEFAULT_PAGE_SIZE = 25

/** The page sizes a reader may choose; the panel refuses any other with a 400. */
const PAGE_SIZES: readonly number[] = [10, 25, 50, 100]

/** How long the search box must be still before its text reaches the URL, in milliseconds. */
const SEARCH_DEBOUNCE_MS = 300

/** Query parameter holding the 1-based page; absent on page 1. */
const PAGE_KEY = 'page'

/** Query parameter holding the page size; absent at the default. */
const PAGE_SIZE_KEY = 'pageSize'

/** Query parameter holding the search text; absent when nothing is searched. */
const SEARCH_KEY = 'search'

/** Query parameter holding the sort key; absent for the server's default order. */
const SORT_KEY = 'sort'

/**
 * Reads one query parameter as a single string; a repeated or missing parameter reads as empty.
 * @param query The route's query.
 * @param key The parameter's name.
 * @returns Its value, or an empty string.
 */
const queryText = (query: LocationQuery, key: string): string => {
  const value = query[key]
  return typeof value === 'string' ? value : ''
}

/**
 * Reads a positive whole number from the query.
 * @param query The route's query.
 * @param key The parameter's name.
 * @param fallback What an absent or malformed parameter reads as.
 * @returns The number, or the fallback when the parameter is not a positive integer.
 */
const queryPositiveInteger = (query: LocationQuery, key: string, fallback: number): number => {
  const text = queryText(query, key)
  if (!/^\d+$/.test(text)) {
    return fallback
  }
  const value = Number(text)
  return value >= 1 && Number.isSafeInteger(value) ? value : fallback
}

/**
 * Server-paged table state for a list page: page, page size, search, sort and typed filters, kept
 * in the URL so a list can be shared, reloaded and walked back through, and fetched through the
 * page's own `load` whenever the URL changes.
 *
 * **The URL is the state.** Every value is read from `route.query` and every change is written with
 * `router.replace`, so there is exactly one copy and the address bar cannot disagree with the
 * screen. A default (page 1, 25 rows, no search, no sort, no filter) is left out of the URL rather
 * than written, so an untouched list has a clean address. Parameters the composable does not own
 * are kept as they are.
 *
 * **What resets the page.** A new search, a new page size, a new sort and any filter change go back
 * to page 1: the reader's position on the old result set means nothing on the new one. A page past
 * the end (rows were deleted, or a stale link) moves to the last page once the total is known; the
 * server answers such a page with no rows and the true total rather than clamping, so the move is
 * this composable's job.
 *
 * **Stale answers.** Only the most recent request's answer is used: a slow page 2 arriving after
 * page 3 was asked for must not set the total.
 * @template T The row type the page's store loads.
 * @param options The filter schema and the store-backed `load`.
 * @returns The list's state and the actions that change it.
 */
export const useServerTable = <T>(options: ServerTableOptions<T>): ServerTable => {
  const schema: ListFilterSchema = options.filters ?? {}

  const route = useRoute()
  const router = useRouter()

  /** The route this table belongs to; a query change on another route is not this list's. */
  const ownPath: string = route.path

  const search: Ref<string> = ref(queryText(route.query, SEARCH_KEY))
  const total: Ref<number> = ref(0)
  const state: Ref<ServerTableState> = ref({ status: 'loading' })

  /** Pending debounce timer for the search box; null when none is waiting. */
  let searchTimer: ReturnType<typeof setTimeout> | null = null
  /** Sequence number of the latest request; an answer carrying an older number is dropped. */
  let latestRequest = 0

  const page: ComputedRef<number> = computed(() => {
    return queryPositiveInteger(route.query, PAGE_KEY, 1)
  })

  const pageSize: ComputedRef<number> = computed(() => {
    const asked = queryPositiveInteger(route.query, PAGE_SIZE_KEY, DEFAULT_PAGE_SIZE)
    return PAGE_SIZES.includes(asked) ? asked : DEFAULT_PAGE_SIZE
  })

  const sort: ComputedRef<string> = computed(() => {
    return queryText(route.query, SORT_KEY)
  })

  const filters: ComputedRef<Record<string, string>> = computed(() => {
    const applied: Record<string, string> = {}
    for (const [key, spec] of Object.entries(schema)) {
      const value = queryText(route.query, key)
      applied[key] = value !== '' && (spec.accepts?.(value) ?? true) ? value : ''
    }
    return applied
  })

  const activeFilterCount: ComputedRef<number> = computed(() => {
    return Object.values(filters.value).filter((value) => {return value !== ''}).length
  })

  const request: ComputedRef<ListRequest> = computed(() => {
    const set = Object.entries(filters.value).filter(([, value]) => {return value !== ''})
    return {
      page: page.value,
      pageSize: pageSize.value,
      search: queryText(route.query, SEARCH_KEY),
      sort: sort.value,
      filters: Object.fromEntries(set),
    }
  })

  const pageCount: ComputedRef<number> = computed(() => {
    return Math.max(1, Math.ceil(total.value / pageSize.value))
  })

  const loadingState: ComputedRef<LoadingState> = computed(() => {
    const status = state.value.status
    return status === 'loading' || status === 'loaded' ? status : 'error'
  })

  /**
   * Writes the given parameters into the URL, dropping each one whose value is its default, and
   * keeping every parameter this composable does not own.
   * @param changes Parameters to set; an empty string removes the parameter (a default is never written).
   * @returns Resolves once the route has changed.
   */
  const writeQuery = async (changes: Record<string, string>): Promise<void> => {
    const query: LocationQueryRaw = { ...route.query }
    for (const [key, value] of Object.entries(changes)) {
      if (value === '') {
        delete query[key]
      } else {
        query[key] = value
      }
    }
    await router.replace({ query })
  }

  /**
   * The query form of a page number: absent for page 1, the default.
   * @param target The page.
   * @returns The parameter's value, or an empty string to drop it.
   */
  const pageParam = (target: number): string => {
    return target > 1 ? String(target) : ''
  }

  /**
   * Moves to a page.
   * @param target The 1-based page.
   * @returns Resolves once the URL has changed.
   */
  const setPage = async (target: number): Promise<void> => {
    await writeQuery({ [PAGE_KEY]: pageParam(target) })
  }

  /**
   * Changes the page size and goes back to page 1.
   * @param target The new page size.
   * @returns Resolves once the URL has changed.
   */
  const setPageSize = async (target: number): Promise<void> => {
    await writeQuery({
      [PAGE_SIZE_KEY]: target === DEFAULT_PAGE_SIZE ? '' : String(target),
      [PAGE_KEY]: '',
    })
  }

  /**
   * Sorts by a field, flipping the direction when it is already the sort, and goes back to page 1.
   * @param field The sort key.
   * @returns Resolves once the URL has changed.
   */
  const toggleSort = async (field: string): Promise<void> => {
    const next = sort.value === field ? `-${field}` : field
    await writeQuery({ [SORT_KEY]: next, [PAGE_KEY]: '' })
  }

  /**
   * Replaces every filter in the schema and goes back to page 1.
   * @param next Values by filter name; an empty or missing value clears that filter.
   * @returns Resolves once the URL has changed.
   */
  const setFilters = async (next: Record<string, string>): Promise<void> => {
    const changes: Record<string, string> = { [PAGE_KEY]: '' }
    // Only keys in the schema are written: a filter sheet cannot smuggle an arbitrary parameter in.
    for (const key of Object.keys(schema)) {
      changes[key] = next[key] ?? ''
    }
    await writeQuery(changes)
  }

  /**
   * Clears one filter and goes back to page 1.
   * @param key The filter's name.
   * @returns Resolves once the URL has changed.
   */
  const clearFilter = async (key: string): Promise<void> => {
    await writeQuery({ [key]: '', [PAGE_KEY]: '' })
  }

  /**
   * Clears every filter and goes back to page 1.
   * @returns Resolves once the URL has changed.
   */
  const clearFilters = async (): Promise<void> => {
    await setFilters({})
  }

  /**
   * Hands a request to the page's `load`, records the total, and moves to the last page when the
   * one asked for lies past it.
   * @param current The request to fetch.
   * @returns Resolves once the answer was handled, or dropped as stale.
   */
  const fetchPage = async (current: ListRequest): Promise<void> => {
    latestRequest += 1
    const sequence = latestRequest
    state.value = { status: 'loading' }
    const outcome = await options.load(current)
    if (sequence !== latestRequest) {
      return
    }
    if (outcome.kind === 'refused') {
      state.value = { status: 'refused', message: outcome.message }
      return
    }
    if (outcome.kind === 'unreachable') {
      state.value = { status: 'unreachable' }
      return
    }
    total.value = outcome.page.total
    state.value = { status: 'loaded' }
    // The server answers a page past the end with no rows and the true total (it never clamps),
    // so the reader is moved to the last page that has rows; the URL change refetches it.
    const lastPage = Math.max(1, Math.ceil(outcome.page.total / current.pageSize))
    if (current.page > lastPage) {
      await setPage(lastPage)
    }
  }

  /**
   * Fetches the current page again.
   * @returns Resolves once the page settled.
   */
  const reload = async (): Promise<void> => {
    await fetchPage(request.value)
  }

  /**
   * Cancels a search that has not reached the URL yet.
   * @returns Nothing.
   */
  const cancelPendingSearch = (): void => {
    if (searchTimer !== null) {
      clearTimeout(searchTimer)
      searchTimer = null
    }
  }

  // Typing reaches the URL once the reader pauses, and a new search starts from page 1.
  watch(search, (text: string): void => {
    cancelPendingSearch()
    if (text === queryText(route.query, SEARCH_KEY)) {
      return
    }
    searchTimer = setTimeout((): void => {
      searchTimer = null
      void writeQuery({ [SEARCH_KEY]: text.trim() === '' ? '' : text, [PAGE_KEY]: '' })
    }, SEARCH_DEBOUNCE_MS)
  })

  // Back, Forward or a link can change the search under the box; the box follows the URL.
  watch(
    (): string => {return queryText(route.query, SEARCH_KEY)},
    (text: string): void => {
      if (route.path !== ownPath) {
        return
      }
      cancelPendingSearch()
      search.value = text
    },
  )

  // The URL is the state: every change to it is a fetch. Compared as JSON so a navigation that
  // leaves the request as it was (another parameter changed) does not refetch.
  watch(
    (): string => {return JSON.stringify(request.value)},
    (): void => {
      // Leaving the page changes the query before this scope is torn down; that is not this list's.
      if (route.path !== ownPath) {
        return
      }
      void fetchPage(request.value)
    },
    { immediate: true },
  )

  onScopeDispose(cancelPendingSearch)

  return {
    request,
    page,
    pageSize,
    pageSizes: PAGE_SIZES,
    search,
    sort,
    filters,
    activeFilterCount,
    total,
    pageCount,
    state,
    loadingState,
    setPage,
    setPageSize,
    toggleSort,
    setFilters,
    clearFilter,
    clearFilters,
    reload,
  }
}
