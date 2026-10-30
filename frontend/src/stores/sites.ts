import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSitesApi } from '../composables/apis/useSitesApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { CreateSiteRequest, Site, SiteDetailLoad, DisabledSitesCount } from '../types/site'
import type { PhpVersion } from '../types/phpVersion'
import type {
  SiteLogEndReason,
  SiteLogLine,
  SiteLogSource,
  SiteLogStreamStatus,
} from '../types/siteLog'

/**
 * How many historical lines a log tail asks for. A UI concern — how much scrollback a person
 * gets on opening the tab — not a domain value, so it lives here (rules/vue.md).
 */
const LOG_HISTORY_LINES = 200

/**
 * The most lines the store keeps in memory for one stream. A log that is appended to for hours
 * would otherwise grow the array without bound and take the tab down with it; the oldest lines
 * are dropped as new ones arrive, which is a scrollback limit and is why the view shows how many
 * lines it holds rather than implying it holds all of them.
 */
const MAX_LOG_LINES = 2000

/**
 * The page size of the read that only counts disabled sites. Its rows are never shown, so the
 * smallest size the panel serves; the total is what is wanted.
 */
const COUNT_PAGE_SIZE = 10

/**
 * Owns the sites list, one site's detail, the host's installed PHP versions, and the live tail of
 * a site's log. The site pages read state from here and call its actions — none of them touches
 * the API composable directly (rules/vue.md: "API composables are called from Pinia stores ONLY").
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

  /** The PHP versions installed on the host, as last loaded. Never a list written into the SPA. */
  const phpVersions: Ref<PhpVersion[]> = ref([])

  /** The version an install is running for, or `null` when none is. */
  const installingPhpVersion: Ref<string | null> = ref(null)

  /**
   * Why the last install attempt failed, verbatim from the panel, or `null`.
   *
   * Separate from the store's general `errorMessage` for the reason the create flow keeps its own:
   * an install that fails must not wipe the reason a different request failed, and the screen
   * shows this one next to the control that caused it.
   */
  const phpInstallErrorMessage: Ref<string | null> = ref(null)

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

  /** The lines of the log currently being tailed, oldest first, capped at {@link MAX_LOG_LINES}. */
  const logLines: Ref<SiteLogLine[]> = ref([])

  /** Whether a log stream is open, has ended, or was never started. */
  const logStatus: Ref<SiteLogStreamStatus> = ref('idle')

  /**
   * Why the last log stream ended, or `null` while one is open or none has run.
   *
   * The whole point of keeping it: a pane that stopped updating looks the same whether the log
   * simply had nothing more to say, was dropped, was truncated, or failed. The view renders this
   * reason, so a truncation is never shown as a normal end.
   */
  const logEndReason: Ref<SiteLogEndReason | null> = ref(null)

  /** The backend's own explanation of a failed log stream, or `null` when it sent none. */
  const logEndMessage: Ref<string | null> = ref(null)

  /**
   * Whether this view's own scrollback cap has already dropped lines the stream delivered.
   *
   * A second, quieter truncation than {@link logEndReason}'s: the stream is intact, but what is
   * held is not all of it. The pane says so, because an operator scrolling to the top of a log
   * must not read the oldest line it holds as the oldest line there was.
   */
  const logScrollbackTruncated: Ref<boolean> = ref(false)

  /**
   * Aborts the open log stream, or `null` when none is open. Held so the store can stop a stream
   * on unmount, on a source switch, and on navigation: a stream nobody aborts keeps its
   * connection open for the life of the tab.
   */
  const logAbort: Ref<AbortController | null> = ref(null)

  /**
   * Identifies the stream whose events are currently welcome. A stream that has been replaced
   * can still deliver one last event before its abort lands, and appending that to the new
   * stream's lines would interleave two logs; comparing this token drops it instead.
   */
  const logStreamToken: Ref<number> = ref(0)

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
   * Loads the PHP versions installed on the host, which the site form selects from.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadPhpVersions = async (): Promise<void> => {
    try {
      phpVersions.value = await api.listPhpVersions()
      errorMessage.value = null
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  /**
   * Asks the server to install a PHP version, and replaces the held list with what it answers.
   *
   * The list is REPLACED by the server's answer rather than appended to with the version that was
   * requested: what this host has is the agent's to report, and a list grown from the request
   * would show a version as installed on the strength of having asked for it.
   * @param version The version to install, e.g. `8.4`.
   * @returns `true` when the server reports the version among those installed afterwards.
   */
  const installPhpVersion = async (version: string): Promise<boolean> => {
    installingPhpVersion.value = version
    try {
      phpVersions.value = await api.installPhpVersion(version)
      phpInstallErrorMessage.value = null
      return phpVersions.value.some((installed) => {
        return installed.version === version
      })
    } catch (error) {
      phpInstallErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      installingPhpVersion.value = null
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

  /**
   * Stops the open log stream, if any, and releases its connection.
   *
   * Safe to call when nothing is open, and safe to call twice — the log tab calls it on unmount,
   * where neither condition can be assumed.
   * @returns Nothing.
   */
  const stopLogTail = (): void => {
    const controller = logAbort.value
    if (controller === null) {
      return
    }

    logAbort.value = null
    controller.abort()
  }

  /**
   * Opens a live tail of one of a site's logs, replacing any stream already open.
   *
   * Resolves when the stream ends, so a caller may await it; the ending itself is left in
   * {@link logStatus}, {@link logEndReason} and {@link logEndMessage} rather than returned,
   * because the pane has to keep showing it after the promise is long settled.
   * @param siteId The site whose log is read.
   * @param source Which of the site's two logs to tail.
   * @returns Resolves once the stream has ended.
   */
  const startLogTail = async (siteId: string, source: SiteLogSource): Promise<void> => {
    // A second tab click must not leave the first stream pulling in the background.
    stopLogTail()

    const controller = new AbortController()
    logAbort.value = controller
    logStreamToken.value += 1
    const token = logStreamToken.value

    logLines.value = []
    logScrollbackTruncated.value = false
    logEndReason.value = null
    logEndMessage.value = null
    logStatus.value = 'streaming'

    await api.tailLog(
      { siteId, source, historyLines: LOG_HISTORY_LINES },
      {
        /**
         * Appends one line, dropping the oldest once the scrollback cap is reached.
         * @param line The line the panel sent.
         * @returns Nothing.
         */
        onLine: (line: SiteLogLine): void => {
          if (token !== logStreamToken.value) {
            return
          }

          const next = [...logLines.value, line]
          if (next.length > MAX_LOG_LINES) {
            // Latched rather than derived from the length: once a line has been dropped the pane
            // is permanently incomplete, and the array stays at the cap so its length alone
            // could not say whether the cap was ever exceeded.
            logScrollbackTruncated.value = true
            logLines.value = next.slice(next.length - MAX_LOG_LINES)
            return
          }
          logLines.value = next
        },

        /**
         * Records why the stream ended, so the view can say so rather than simply stopping.
         * @param reason Why the stream ended.
         * @param message The backend's explanation, or null.
         * @returns Nothing.
         */
        onEnd: (reason: SiteLogEndReason, message: string | null): void => {
          if (token !== logStreamToken.value) {
            return
          }

          logEndReason.value = reason
          logEndMessage.value = message
          logStatus.value = 'ended'
          logAbort.value = null
        },
      },
      controller.signal,
    )
  }

  return {
    sites,
    disabledCount,
    detail,
    phpVersions,
    installingPhpVersion,
    phpInstallErrorMessage,
    creating,
    acting,
    errorMessage,
    createErrorMessage,
    logLines,
    logStatus,
    logEndReason,
    logEndMessage,
    logScrollbackTruncated,
    list,
    loadDisabledCount,
    loadOne,
    loadPhpVersions,
    installPhpVersion,
    create,
    changePhpVersion,
    enable,
    disable,
    remove,
    startLogTail,
    stopLogTail,
  }
})
