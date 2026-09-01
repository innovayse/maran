import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSitesApi } from '../composables/apis/useSitesApi'
import { ApiError } from '../composables/useApi'
import type { CreateSiteRequest, Site, SiteDetail } from '../types/site'
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
 * Owns the sites list, one site's detail, the host's installed PHP versions, and the live tail of
 * a site's log. The site pages read state from here and call its actions — none of them touches
 * the API composable directly (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useSitesStore = defineStore('sites', () => {
  const api = useSitesApi()

  /** The sites as last reported by the panel; empty before the first successful load. */
  const sites: Ref<Site[]> = ref([])

  /** The site the detail page is showing, or `null` before one is loaded. */
  const selected: Ref<SiteDetail | null> = ref(null)

  /** The PHP versions installed on the host, as last loaded. Never a list written into the SPA. */
  const phpVersions: Ref<PhpVersion[]> = ref([])

  /** True while a read request (the list, one site, or the versions) is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (version change, enable, disable, delete) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** True once the list has been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

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
   * Loads the site list, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      sites.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Loads one site into {@link selected}, replacing whatever was held.
   * @param id The site to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    loading.value = true
    try {
      selected.value = await api.get(id)
      errorMessage.value = null
    } catch (error) {
      selected.value = null
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
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
   * Creates a site and, on success, adds it to the held list so the list page reflects it
   * without a full reload.
   * @param request The site's account, domain, aliases and backend.
   * @returns The created site, or `null` when the request failed — the caller reads
   * {@link createErrorMessage} for the reason.
   */
  const create = async (request: CreateSiteRequest): Promise<Site | null> => {
    creating.value = true
    try {
      const created = await api.create(request)
      sites.value = [...sites.value, created]
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
   * Folds a site the panel returned back into both the held list and {@link selected}, so a list
   * open behind a detail page does not go stale.
   * @param changed The site in its new state.
   * @returns Nothing.
   */
  const absorb = (changed: Site): void => {
    sites.value = sites.value.map((site) => {
      return site.id === changed.id ? changed : site
    })

    const current = selected.value
    if (current !== null && current.id === changed.id) {
      // The list-shaped result carries only part of the detail, so the held detail is patched
      // field by field: replacing it wholesale would drop the document root and the aliases the
      // overview is showing.
      selected.value = {
        ...current,
        backendType: changed.backendType,
        phpVersion: changed.phpVersion,
        status: changed.status,
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
      selected.value = null
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
    selected,
    phpVersions,
    loading,
    creating,
    acting,
    isLoaded,
    errorMessage,
    createErrorMessage,
    logLines,
    logStatus,
    logEndReason,
    logEndMessage,
    logScrollbackTruncated,
    load,
    loadOne,
    loadPhpVersions,
    create,
    changePhpVersion,
    enable,
    disable,
    remove,
    startLogTail,
    stopLogTail,
  }
})
