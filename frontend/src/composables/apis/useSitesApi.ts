import { ApiError, useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  ChangeSiteLogRetentionRequest,
  ChangeSitePhpVersionRequest,
  CreateSiteRequest,
  Site,
  SiteChoices,
  SiteDetail,
  SiteLimits,
  SitesApi,
} from '../../types/site'
import type {
  PhpExtension,
  PhpVersion,
  SetPhpExtensionRequest,
} from '../../types/phpVersion'
import type { SiteTraffic } from '../../types/siteTraffic'
import type {
  SiteLogEndReason,
  SiteLogStreamHandlers,
  TailSiteLogOptions,
} from '../../types/siteLog'

/** The endpoint sites are listed, created and read through. */
const SITES_PATH = '/api/v1/sites'

/** The list endpoint's parameter for each filter the sites list page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  account: 'accountId',
  backend: 'backendType',
  phpVersion: 'phpVersion',
  status: 'status',
}

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
   * Lists one page of the sites the caller may see. The server ANDs an account filter with its
   * own tenant filter, so a customer naming another account receives an empty page.
   * @param request The page, size, search, sort and the account, backend, PHP and status filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<Site>> => {
    return api.get<Paged<Site>>(`${SITES_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
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
   * @returns Resolves once the panel removed the site; the answer is an empty 204.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${SITES_PATH}/${id}`, signal)
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
   * Reads the bounds a site's form must respect.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The limits, as the server states them.
   */
  const getLimits = (signal?: AbortSignal): Promise<SiteLimits> => {
    return api.get<SiteLimits>(`${SITES_PATH}/limits`, signal)
  }

  /**
   * Reads the closed sets the site form and list filters select from.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The backends and serving states, labelled by the server.
   */
  const getChoices = (signal?: AbortSignal): Promise<SiteChoices> => {
    return api.get<SiteChoices>(`${SITES_PATH}/choices`, signal)
  }

  /**
   * Lists the extensions of one installed PHP version.
   *
   * The labels and, for an administrator, what can be done to each come from the server: the SPA
   * holds no catalogue of extensions of its own (rules/vue.md, "the frontend never invents domain
   * data").
   * @param version The installed version, e.g. `8.3`.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The version's extensions.
   */
  const listPhpExtensions = (version: string, signal?: AbortSignal): Promise<PhpExtension[]> => {
    return api.get<PhpExtension[]>(`${SITES_PATH}/php-versions/${encodeURIComponent(version)}/extensions`, signal)
  }

  /**
   * Switches one extension of a PHP version on or off for the whole host.
   *
   * Administrator-only on the server, which refuses anyone else; this call does not pretend
   * otherwise (rules/security.md, a check in the browser is advice). The request does not resolve
   * until the change is applied, and its progress is on the tasks screen.
   * @param version The installed version, e.g. `8.3`.
   * @param name The extension's catalogue name, as listed.
   * @param enabled The state to leave it in.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The version's extensions as the server reads them back after the change.
   */
  const setPhpExtension = (
    version: string,
    name: string,
    enabled: boolean,
    signal?: AbortSignal,
  ): Promise<PhpExtension[]> => {
    const body: SetPhpExtensionRequest = { enabled }
    return api.put<PhpExtension[]>(
      `${SITES_PATH}/php-versions/${encodeURIComponent(version)}/extensions/${encodeURIComponent(name)}`,
      body,
      signal,
    )
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
   * that fails ends as `failed`, an `end` frame naming a reason this SPA does not know ends as
   * `failed`, and a connection that closes without any `end` frame at all ends as `truncated` —
   * the two silences are told apart because only the second one means lines may be missing.
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
            const payload = JSON.parse(event.data) as { reason?: string; message: string | null }
            ending = { reason: readEndReason(payload.reason), message: payload.message }
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

  /**
   * Reads a site's traffic summary over a window. A site belonging to somebody else answers 404,
   * never 403; a site with nothing recorded yet answers 200 with `hasData: false`.
   * @param id The site's identity.
   * @param days The window in days, one of the windows the choices offer.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The summary the panel built.
   */
  const getTraffic = (id: string, days: number, signal?: AbortSignal): Promise<SiteTraffic> => {
    return api.get<SiteTraffic>(`${SITES_PATH}/${id}/traffic?days=${days}`, signal)
  }

  /**
   * Changes how many days of rotated logs a site keeps. The panel saves the choice and removes the
   * archives past it later; the answer never waits for that.
   * @param id The site's identity.
   * @param days The retention in days, one of the values the choices offer.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site in its new state.
   */
  const changeLogRetention = (id: string, days: number, signal?: AbortSignal): Promise<Site> => {
    const body: ChangeSiteLogRetentionRequest = { days }
    return api.put<Site>(`${SITES_PATH}/${id}/log-retention`, body, signal)
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
    getLimits,
    getChoices,
    listPhpExtensions,
    setPhpExtension,
    tailLog,
    getTraffic,
    changeLogRetention,
  }
}
