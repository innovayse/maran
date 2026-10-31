import type { ListRequest, Paged } from './list'
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
  /** The owning account's short name, resolved by the panel so the list never joins accounts itself. */
  ownerName: string
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
   * Reads the bounds a site's form must respect, so the form shows the server's limit rather than
   * a copy of it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The limits.
   */
  getLimits: (signal?: AbortSignal) => Promise<SiteLimits>

  /**
   * Installs a PHP version on the server and answers what is installed afterwards.
   *
   * Administrator-only on the server. The promise does not settle until the install finishes,
   * which is the host's package manager fetching over whatever mirror it has.
   * @param version The version to install, e.g. `8.4`.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The versions installed on this server after the attempt.
   */
  installPhpVersion: (version: string, signal?: AbortSignal) => Promise<PhpVersion[]>

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
