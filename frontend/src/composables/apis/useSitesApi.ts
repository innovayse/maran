import { ApiError, useApi } from '../useApi'
import type {
  ChangeSitePhpVersionRequest,
  CreateSiteRequest,
  Site,
  SiteDetail,
  SitesApi,
} from '../../types/site'
import type { PhpVersion } from '../../types/phpVersion'
import type {
  SiteLogEndReason,
  SiteLogStreamHandlers,
  TailSiteLogOptions,
} from '../../types/siteLog'

/** The endpoint sites are listed, created and read through. */
const SITES_PATH = '/api/v1/sites'

/**
 * The endings the panel may name on a log stream. Used to check what arrived on the wire: an
 * unrecognised ending is reported as `failed` rather than as a normal end, because the one
 * outcome this whole path exists to prevent is an operator being told a truncated log finished.
 */
const END_REASONS: readonly SiteLogEndReason[] = [
  'completed',
  'dropped',
  'idle',
  'failed',
  'truncated',
  'cancelled',
]

/**
 * Builds the sites API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry
 * in the returned object: the name is what appears in a stack trace, and the doc block sits next
 * to the call it describes (rules/vue.md).
 * @returns The {@link SitesApi} bound to the panel's sites endpoints.
 */
export const useSitesApi = (): SitesApi => {
  const api = useApi()

  /**
   * Lists the sites the caller may see.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The sites, in the order the panel reports them.
   */
  const list = (signal?: AbortSignal): Promise<Site[]> => {
    return api.get<Site[]>(SITES_PATH, signal)
  }

  /**
   * Reads one site. A site belonging to somebody else answers 404, never 403.
   * @param id The site's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site's full detail.
   */
  const get = (id: string, signal?: AbortSignal): Promise<SiteDetail> => {
    return api.get<SiteDetail>(`${SITES_PATH}/${id}`, signal)
  }

  /**
   * Creates a site.
   * @param request The site's account, domain, aliases and backend.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site as the panel created it.
   */
  const create = (request: CreateSiteRequest, signal?: AbortSignal): Promise<Site> => {
    return api.post<Site>(SITES_PATH, request, signal)
  }

  /**
   * Rebinds a site to a different installed PHP version.
   * @param id The site's identity.
   * @param phpVersion The installed version to switch to.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site in its new state.
   */
  const changePhpVersion = (
    id: string,
    phpVersion: string,
    signal?: AbortSignal,
  ): Promise<Site> => {
    const body: ChangeSitePhpVersionRequest = { phpVersion }
    return api.post<Site>(`${SITES_PATH}/${id}/php-version`, body, signal)
  }

  /**
   * Returns a disabled site to serving its own content. Idempotent.
   * @param id The site's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site in its new state.
   */
  const enable = (id: string, signal?: AbortSignal): Promise<Site> => {
    return api.post<Site>(`${SITES_PATH}/${id}/enable`, undefined, signal)
  }

  /**
   * Makes a site serve a suspension response instead of its content. Idempotent.
   * @param id The site's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site in its new state.
   */
  const disable = (id: string, signal?: AbortSignal): Promise<Site> => {
    return api.post<Site>(`${SITES_PATH}/${id}/disable`, undefined, signal)
  }

  /**
   * Removes a site's vhost and its row; the customer's files are left alone.
   * @param id The site's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed the site.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${SITES_PATH}/${id}`, signal)
  }

  /**
   * Lists the PHP versions installed on this server.
   *
   * The site form selects from this rather than from a list written into the SPA: the versions
   * are whatever the host has, which only the agent knows (rules/vue.md, "the frontend never
   * invents domain data").
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The installed versions.
   */
  const listPhpVersions = (signal?: AbortSignal): Promise<PhpVersion[]> => {
    return api.get<PhpVersion[]>(`${SITES_PATH}/php-versions`, signal)
  }

  /**
   * Reads the ending named in an `end` frame, refusing to invent a benign one.
   * @param named The `reason` the frame carried, or `undefined` when it carried none.
   * @returns The ending the panel named, or `failed` when it named none this SPA knows.
   */
  const readEndReason = (named: string | undefined): SiteLogEndReason => {
    const known = END_REASONS.find((reason) => {
      return reason === named
    })
    return known ?? 'failed'
  }

  /**
   * Tails one of a site's logs over SSE: recent lines first, then new ones as they are written.
   *
   * Exactly one `onEnd` call is made per stream, whatever happened — the server's own ending, an
   * abort, or a transport failure — so the caller never has to infer that a stream is over from
   * lines having stopped arriving. An ending is never upgraded to a friendlier one: a stream
   * that fails ends as `failed`, and one nobody named ends as `failed` too.
   * @param options The site, the log and how much history to replay.
   * @param handlers Where lines and the ending are delivered.
   * @param signal Abort signal that stops the stream and releases its connection.
   * @returns Resolves once the stream has ended and `onEnd` has been called.
   */
  const tailLog = async (
    options: TailSiteLogOptions,
    handlers: SiteLogStreamHandlers,
    signal: AbortSignal,
  ): Promise<void> => {
    const query = new URLSearchParams({
      source: options.source,
      historyLines: String(options.historyLines),
    })
    const path = `${SITES_PATH}/${options.siteId}/logs?${query.toString()}`

    // Set as soon as the server names an ending, so the natural close that follows it does not
    // overwrite the reason with a generic one.
    let ending: { reason: SiteLogEndReason; message: string | null } | null = null

    try {
      await api.stream(
        path,
        (event) => {
          if (event.name === 'line') {
            const payload = JSON.parse(event.data) as { line?: string; historical?: boolean }
            handlers.onLine({ line: payload.line ?? '', historical: payload.historical === true })
            return
          }

          if (event.name === 'end') {
            const payload = JSON.parse(event.data) as { reason?: string; message?: string }
            ending = { reason: readEndReason(payload.reason), message: payload.message ?? null }
          }
        },
        signal,
      )
    } catch (error) {
      // An abort is the panel deciding to stop watching, not a failure of the log.
      if (signal.aborted) {
        handlers.onEnd('cancelled', null)
        return
      }

      handlers.onEnd('failed', error instanceof ApiError ? error.message : null)
      return
    }

    if (ending !== null) {
      const named: { reason: SiteLogEndReason; message: string | null } = ending
      handlers.onEnd(named.reason, named.message)
      return
    }

    // The connection closed without the panel naming an ending. That is precisely the silent
    // truncation an operator must not be shown as a normal end, so it is reported as one.
    handlers.onEnd('truncated', null)
  }

  return {
    list,
    get,
    create,
    changePhpVersion,
    enable,
    disable,
    remove,
    listPhpVersions,
    tailLog,
  }
}
