import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSitesApi } from '../composables/apis/useSitesApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { CreateSiteRequest, Site, SiteDetailLoad, SiteLimits, DisabledSitesCount } from '../types/site'

/**
 * The page size of the read that only counts disabled sites. Its rows are never shown, so the
 * smallest size the panel serves; the total is what is wanted.
 */
const COUNT_PAGE_SIZE = 10
/**
 * Owns the sites list, one site's detail, the site form's bounds, and the site mutations. The
 * host's PHP versions live in `phpVersions.ts` and the log tail in `siteLogs.ts`. The site pages
 * read state from here and call its actions — none of them touches the API composable directly
 * (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useSitesStore = defineStore('sites', () => {
  const api = useSitesApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const sites: Ref<Site[]> = ref([])

  /**
   * How many sites are disabled among those the list is narrowed to by account, counted by the
   * panel across every page, with whether the count could be taken.
   */
  const disabledCount: Ref<DisabledSitesCount> = ref({ state: 'loading' })

  /** What the detail page last learned about the site it asked for: loading, found, not found or failed. */
  const detail: Ref<SiteDetailLoad> = ref({ state: 'loading' })

  /**
   * The bounds the site form shows, or `null` until the server has answered. A form that could not
   * read them stays permissive: the server enforces the bounds, the form only advises.
   */
  const limits: Ref<SiteLimits | null> = ref(null)

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (version change, enable, disable, delete) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed read or mutation, or `null` when the
   * last one succeeded or none has been attempted. Rendered verbatim.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null`. Kept apart
   * from {@link errorMessage} so a rejected form does not blank the list page's own error.
   */
  const createErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads one page of the site list into {@link sites} and reports how the read ended, for the
   * list page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<Site>> => {
    try {
      const page = await api.list(request)
      sites.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Asks the panel how many sites are disabled, narrowed to one account when one is given, into
   * {@link disabledCount}. The count is the total of a list read filtered to disabled sites, so it
   * covers every page, not only the one on screen. A failure is recorded as `unavailable`, never as
   * a zero, so the notice can say the count is missing rather than imply there is nothing to count.
   * @param accountId The account the list is narrowed to; empty for every site the caller may see.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadDisabledCount = async (accountId: string): Promise<void> => {
    const filters: Record<string, string> = { status: 'disabled' }
    if (accountId !== '') {
      filters.account = accountId
    }
    try {
      const page = await api.list({ page: 1, pageSize: COUNT_PAGE_SIZE, search: '', sort: '', filters })
      disabledCount.value = { state: 'counted', count: page.total }
    } catch {
      disabledCount.value = { state: 'unavailable' }
    }
  }

  /**
   * Loads one site into {@link detail}, replacing whatever was held.
   *
   * A 404 is an answer, not a failure: the panel says no such site exists for the caller (deleted,
   * another account's, or never created), and the page shows its not-found state for it. Any other
   * refusal keeps the panel's own reason, and a request that got no answer at all keeps none.
   * @param id The site to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    detail.value = { state: 'loading' }
    try {
      detail.value = { state: 'found', site: await api.get(id) }
      errorMessage.value = null
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        detail.value = { state: 'notFound' }
      } else {
        detail.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      }
    }
  }

  /**
   * Loads the bounds the site form shows. A failure leaves `limits` null rather than raising an
   * error of its own: the bound is advice here, and the server's refusal is what the operator reads.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadLimits = async (): Promise<void> => {
    try {
      limits.value = await api.getLimits()
    } catch {
      limits.value = null
    }
  }

  /**
   * Creates a site. The list page is not patched: where the new row falls depends on the server's
   * sort and filters, so the list page reads its page again when it opens.
   * @param request The site's account, domain, aliases and backend.
   * @returns The created site, or `null` when the request failed — the caller reads
   * {@link createErrorMessage} for the reason.
   */
  const create = async (request: CreateSiteRequest): Promise<Site | null> => {
    creating.value = true
    try {
      const created = await api.create(request)
      createErrorMessage.value = null
      return created
    } catch (error) {
      // Validation, plan-limit and conflict errors arrive already localized; stored verbatim
      // rather than replaced with frontend copy that would hide the reason.
      createErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      creating.value = false
    }
  }

  /**
   * Folds a site the panel returned back into both the held list and {@link detail}, so a list
   * open behind a detail page does not go stale.
   * @param changed The site in its new state.
   * @returns Nothing.
   */
  const absorb = (changed: Site): void => {
    sites.value = sites.value.map((site) => {
      return site.id === changed.id ? changed : site
    })

    const current = detail.value
    if (current.state === 'found' && current.site.id === changed.id) {
      // The list-shaped result carries only part of the detail, so the held detail is patched
      // field by field: replacing it wholesale would drop the document root and the aliases the
      // overview is showing.
      detail.value = {
        state: 'found',
        site: {
          ...current.site,
          backendType: changed.backendType,
          phpVersion: changed.phpVersion,
          status: changed.status,
        },
      }
    }
  }

  /**
   * Runs one mutation and folds the site it returns back into the held state.
   * @param call The mutation to make.
   * @returns True when the panel accepted the change.
   */
  const mutate = async (call: () => Promise<Site>): Promise<boolean> => {
    acting.value = true
    try {
      absorb(await call())
      errorMessage.value = null
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Rebinds a site to a different installed PHP version.
   * @param id The site to rebind.
   * @param phpVersion The installed version to switch to.
   * @returns True when the panel accepted the change.
   */
  const changePhpVersion = async (id: string, phpVersion: string): Promise<boolean> => {
    return await mutate(() => {
      return api.changePhpVersion(id, phpVersion)
    })
  }

  /**
   * Returns a disabled site to serving its own content.
   * @param id The site to enable.
   * @returns True when the panel accepted the change.
   */
  const enable = async (id: string): Promise<boolean> => {
    return await mutate(() => {
      return api.enable(id)
    })
  }

  /**
   * Makes a site serve a suspension response instead of its content.
   * @param id The site to disable.
   * @returns True when the panel accepted the change.
   */
  const disable = async (id: string): Promise<boolean> => {
    return await mutate(() => {
      return api.disable(id)
    })
  }

  /**
   * Removes a site, dropping it from the held list. The customer's files stay on disk.
   * @param id The site to remove.
   * @returns True when the panel accepted the removal.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      sites.value = sites.value.filter((site) => {
        return site.id !== id
      })
      detail.value = { state: 'notFound' }
      errorMessage.value = null
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return {
    sites,
    disabledCount,
    detail,
    limits,
    creating,
    acting,
    errorMessage,
    createErrorMessage,
    list,
    loadDisabledCount,
    loadOne,
    loadLimits,
    create,
    changePhpVersion,
    enable,
    disable,
    remove,
  }
})
