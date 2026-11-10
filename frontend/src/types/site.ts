import type { ListRequest, Paged } from './list'
import type { PhpExtension, PhpVersion } from './phpVersion'
import type { SiteLogStreamHandlers, TailSiteLogOptions } from './siteLog'
import type { SiteTraffic } from './siteTraffic'

/**
 * What serves a site's content, mirroring the backend's
 * `Maran.Modules.Sites.Domain.Enums.SiteBackendType`. The API serializes enums as camelCase
 * strings (panel-wide `JsonStringEnumConverter`), so these are the camelCase forms of the C#
 * member names.
 *
 * There is deliberately no "unspecified" member: a site whose backend cannot be named is a site
 * whose vhost cannot be rendered.
 */
export type SiteBackendType = 'static' | 'php' | 'node' | 'reverseProxy'

/**
 * Whether a site serves its own content, mirroring the backend's
 * `Maran.Modules.Sites.Domain.Enums.SiteStatus`.
 *
 * A serving state, not a lifecycle state: a disabled site keeps its vhost, its aliases and its
 * log paths, and answers with a suspension response. A site that no longer exists has no row.
 */
export type SiteStatus = 'enabled' | 'disabled'

/**
 * Outward, list-shaped view of a site, mirroring the backend's `SiteDto` field-for-field.
 *
 * Without the document root, on purpose: the backend keeps a filesystem path out of its
 * widest-read surface, and {@link SiteDetail} carries it for the single-site read.
 */
export interface Site {
  /** The site's identity. */
  id: string
  /** The account that owns this site. */
  accountId: string
  /** The owning account's short name, resolved by the panel so the list never joins accounts itself. */
  ownerName: string
  /** The primary domain served by this site. */
  domain: string
  /** Which backend serves this site's content. */
  backendType: SiteBackendType
  /** The backend as an operator reads it, in the request's language. */
  backendTypeName: string
  /** The bound PHP version, or the empty string when the backend is not PHP. */
  phpVersion: string
  /** Whether the site serves its own content or a suspension response. */
  status: SiteStatus
  /** The serving state as an operator reads it, in the request's language. */
  statusName: string
  /** The instant the site was created, as an ISO-8601 string. */
  createdAt: string
  /** How many days of rotated access and error logs the site keeps. */
  logRetentionDays: number
}

/**
 * Single-site view, mirroring the backend's `SiteDetailDto` field-for-field. It is a superset of
 * {@link Site}, but is not declared as one: the two are separate contracts on the backend, and
 * tying them here would make an additive change to one silently change the other.
 */
export interface SiteDetail {
  /** The site's identity. */
  id: string
  /** The account that owns this site. */
  accountId: string
  /** The primary domain served by this site. */
  domain: string
  /** Additional hostnames answered by the same vhost. */
  aliases: string[]
  /** Which backend serves this site's content. */
  backendType: SiteBackendType
  /** The backend as an operator reads it, in the request's language. */
  backendTypeName: string
  /** The bound PHP version, or the empty string when the backend is not PHP. */
  phpVersion: string
  /** The upstream forwarded to, or the empty string when the backend is not a reverse proxy. */
  proxyUpstream: string
  /** The absolute document root the agent allocated. */
  documentRoot: string
  /** Whether a TLS certificate is currently installed for this site. */
  hasCertificate: boolean
  /** Whether the site serves its own content or a suspension response. */
  status: SiteStatus
  /** The serving state as an operator reads it, in the request's language. */
  statusName: string
  /** The instant the site was created, as an ISO-8601 string. */
  createdAt: string
  /** How many days of rotated access and error logs the site keeps. */
  logRetentionDays: number
}

/**
 * Request body for `POST /api/v1/sites`, binding the backend's `CreateSiteCommand`
 * field-for-field. The caller's address and user agent are NOT here: the backend reads those
 * from the connection, so the request being audited cannot set them.
 */
export interface CreateSiteRequest {
  /** The account that will own the site. */
  accountId: string
  /** The primary domain the site serves. */
  domain: string
  /** Additional hostnames answered by the same vhost. */
  aliases: string[]
  /** Which backend serves the site's content. */
  backendType: SiteBackendType
  /** The installed PHP version to bind to; required when the backend is PHP. */
  phpVersion: string
  /** The upstream to forward to; required when the backend is a reverse proxy. */
  proxyUpstream: string
  /** The installed Node.js major to run under; required when the backend is Node.js, otherwise the empty string. */
  nodeVersion: string
  /** The loopback port the Node.js app listens on (1024 to 65535); required when the backend is Node.js, otherwise `0`. */
  appPort: number
  /** The command that starts the Node.js app, run under the site's own service; required when the backend is Node.js, otherwise the empty string. */
  startCommand: string
}

/**
 * Request body for `POST /api/v1/sites/{id}/php-version`, binding the backend's
 * `ChangeSitePhpVersionCommand`.
 */
export interface ChangeSitePhpVersionRequest {
  /** The installed version to switch to. */
  phpVersion: string
}

/**
 * Request body for `PUT /api/v1/sites/{id}/log-retention`, binding the backend's
 * `ChangeSiteLogRetentionCommand`.
 */
export interface ChangeSiteLogRetentionRequest {
  /** How many days of rotated logs to keep; the panel states the allowed values in its choices. */
  days: number
}

/**
 * Typed access to the sites endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface SitesApi {
  /**
   * Lists one page of the sites the caller may see. The request's `account`, `backend`,
   * `phpVersion` and `status` filters are sent as the endpoint's `accountId`, `backendType`,
   * `phpVersion` and `status`; enum values travel in the camelCase the panel answers with
   * (`reverseProxy`, `disabled`).
   * @param request The page, size, search, sort and filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<Site>>

  /**
   * Reads one site. Another customer's site answers 404, never 403.
   * @param id The site's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site's full detail.
   */
  get: (id: string, signal?: AbortSignal) => Promise<SiteDetail>

  /**
   * Creates a site: its document root, vhost and pool on the host, then the row.
   * @param request The site's account, domain, aliases and backend.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site as the panel created it.
   */
  create: (request: CreateSiteRequest, signal?: AbortSignal) => Promise<Site>

  /**
   * Rebinds a site to a different installed PHP version.
   * @param id The site's identity.
   * @param phpVersion The installed version to switch to.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site in its new state.
   */
  changePhpVersion: (id: string, phpVersion: string, signal?: AbortSignal) => Promise<Site>

  /**
   * Returns a disabled site to serving its own content. Idempotent.
   * @param id The site's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site in its new state.
   */
  enable: (id: string, signal?: AbortSignal) => Promise<Site>

  /**
   * Makes a site serve a suspension response instead of its content. Idempotent.
   * @param id The site's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site in its new state.
   */
  disable: (id: string, signal?: AbortSignal) => Promise<Site>

  /**
   * Removes a site's vhost and its row. The customer's files are left alone.
   * @param id The site's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>

  /**
   * Lists the PHP versions installed on this server — the reference data the site form selects
   * from, so nobody types a version the host does not have.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The installed versions.
   */
  listPhpVersions: (signal?: AbortSignal) => Promise<PhpVersion[]>

  /**
   * Reads the bounds a site's form must respect, so the form shows the server's limit rather than
   * a copy of it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The limits.
   */
  getLimits: (signal?: AbortSignal) => Promise<SiteLimits>

  /**
   * Reads the closed sets the site form and the list's filters select from — the backends and the
   * serving states — each option already labelled in the caller's language by the panel.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The choices.
   */
  getChoices: (signal?: AbortSignal) => Promise<SiteChoices>

  /**
   * Lists the extensions of one installed PHP version. An administrator receives what can be done
   * to each; a customer receives the same list with `availability` left `null`.
   * @param version The installed version, e.g. `8.3`.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The version's extensions, labelled by the server.
   */
  listPhpExtensions: (version: string, signal?: AbortSignal) => Promise<PhpExtension[]>

  /**
   * Switches one extension of a PHP version on or off, for every account on the host.
   *
   * Administrator-only on the server. The promise does not settle until the change has been applied
   * and PHP reloaded, which when the extension's package must be installed first is the package
   * manager's time; the panel records a task for it.
   * @param version The installed version, e.g. `8.3`.
   * @param name The extension's catalogue name, as listed.
   * @param enabled The state to leave it in.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The version's extensions as the server reads them back after the change.
   */
  setPhpExtension: (
    version: string,
    name: string,
    enabled: boolean,
    signal?: AbortSignal,
  ) => Promise<PhpExtension[]>

  /**
   * Tails one of a site's logs: recent lines first, then new ones as they are written.
   *
   * Resolves only once the stream has ended and `onEnd` has been called, so a caller can await
   * teardown; aborting the signal ends the stream with `cancelled` rather than throwing.
   * @param options The site, the log and how much history to replay.
   * @param handlers Where lines and the ending are delivered.
   * @param signal Abort signal that stops the stream and releases the connection.
   * @returns Resolves once the stream has ended.
   */
  tailLog: (
    options: TailSiteLogOptions,
    handlers: SiteLogStreamHandlers,
    signal: AbortSignal,
  ) => Promise<void>

  /**
   * Reads a site's traffic summary over a window.
   * @param id The site's identity.
   * @param days The window in days; one of the windows the panel offers in its choices.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The summary; `hasData: false` when nothing has been recorded yet.
   */
  getTraffic: (id: string, days: number, signal?: AbortSignal) => Promise<SiteTraffic>

  /**
   * Changes how many days of rotated logs a site keeps.
   * @param id The site's identity.
   * @param days The retention in days; one of the values the panel offers in its choices.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The site in its new state.
   */
  changeLogRetention: (id: string, days: number, signal?: AbortSignal) => Promise<Site>
}

/**
 * How many sites are disabled, as the sites list's notice reads it: still being counted, counted,
 * or not countable because the panel did not answer. A count that could not be taken is its own
 * state, never a zero, so the notice can say "could not be counted" instead of saying nothing.
 */
export type DisabledSitesCount =
  | {
      /** The count has not arrived yet. */
      state: 'loading'
    }
  | {
      /** The panel answered. */
      state: 'counted'
      /** Disabled sites across every page of the narrowed list. */
      count: number
    }
  | {
      /** The panel refused or did not answer. */
      state: 'unavailable'
    }

/**
 * What the site detail page last learned about the site it was asked for. Each answer is its own
 * state rather than a `null` site beside a message, so "the panel says there is no such site" and
 * "the panel failed to answer" cannot be confused: the first is the not-found state, the second an
 * error.
 */
export type SiteDetailLoad =
  | {
      /** Nothing has been asked for yet, or the question is still in flight. */
      state: 'loading'
    }
  | {
      /** The panel answered with the site. */
      state: 'found'
      /** The site, patched in place as mutations on it succeed. */
      site: SiteDetail
    }
  | {
      /** The panel answered 404: no site with this id exists for the caller. */
      state: 'notFound'
    }
  | {
      /** The panel refused for another reason, or did not answer. */
      state: 'failed'
      /** The panel's already-localized reason, or `null` when it did not answer at all. */
      message: string | null
    }

/**
 * The bounds a site's form must respect, as the server states them (`GET /api/v1/sites/limits`).
 * The form shows them as advice; the server enforces them.
 */
export interface SiteLimits {
  /** The most aliases one site may carry: every name one certificate covers but the domain's own. */
  maximumAliases: number
}

/** One option of a closed set a site screen selects from (`GET /api/v1/sites/choices`). */
export interface SiteChoice {
  /** The value the API accepts and serializes for this option, e.g. `reverseProxy`. */
  value: string
  /** The option's display name in the caller's language, produced by the panel. */
  label: string
}

/**
 * The closed sets the site screens select from, as the server states them. The server keeps
 * validating every submitted value against the same sets; the lists are advice for the pickers.
 */
export interface SiteChoices {
  /** What can serve a site, in the order the form offers them. */
  backendTypes: SiteChoice[]
  /** Whether a site serves its own content, in the order the filter offers them. */
  statuses: SiteChoice[]
  /** The traffic windows the traffic tab offers, in days, in the order they read. */
  trafficWindows: SiteChoice[]
  /** The log retentions a site may keep, in days, in the order they read. */
  logRetentions: SiteChoice[]
  /** The PHP versions installed on this server, which a PHP site may bind to. */
  phpVersions: SiteChoice[]
  /** The Node.js majors installed on this server, which a Node.js site may run under. */
  nodeVersions: SiteChoice[]
  /** The web server installed on this server, which serves every site; `null` while none is. */
  webServer: SiteChoice | null
}
