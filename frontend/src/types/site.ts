import type { PhpVersion } from './phpVersion'
import type { SiteLogStreamHandlers, TailSiteLogOptions } from './siteLog'

/**
 * What serves a site's content, mirroring the backend's
 * `Maran.Modules.Sites.Domain.Enums.SiteBackendType`. The API serializes enums as camelCase
 * strings (panel-wide `JsonStringEnumConverter`), so these are the camelCase forms of the C#
 * member names.
 *
 * There is deliberately no "unspecified" member: a site whose backend cannot be named is a site
 * whose vhost cannot be rendered.
 */
export type SiteBackendType = 'static' | 'php' | 'reverseProxy'

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
  /** The primary domain served by this site. */
  domain: string
  /** Which backend serves this site's content. */
  backendType: SiteBackendType
  /** The bound PHP version, or the empty string when the backend is not PHP. */
  phpVersion: string
  /** Whether the site serves its own content or a suspension response. */
  status: SiteStatus
  /** The instant the site was created, as an ISO-8601 string. */
  createdAt: string
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
  /** The instant the site was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/sites`, mirroring the backend's `CreateSiteRequest`
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
}

/**
 * Request body for `POST /api/v1/sites/{id}/php-version`, mirroring the backend's
 * `ChangeSitePhpVersionRequest`.
 */
export interface ChangeSitePhpVersionRequest {
  /** The installed version to switch to. */
  phpVersion: string
}

/**
 * Typed access to the sites endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface SitesApi {
  /**
   * Lists the sites the caller may see.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The sites, in the order the panel reports them.
   */
  list: (signal?: AbortSignal) => Promise<Site[]>

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
   * @returns Whether the panel removed the site.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<boolean>

  /**
   * Lists the PHP versions installed on this server — the reference data the site form selects
   * from, so nobody types a version the host does not have.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The installed versions.
   */
  listPhpVersions: (signal?: AbortSignal) => Promise<PhpVersion[]>

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
}
