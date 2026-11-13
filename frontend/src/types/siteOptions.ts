import type { SiteChoice } from './site'

/**
 * Which of a site's apex / "www." pair of names is canonical, mirroring the backend's
 * `CanonicalHost` as the API serializes it (camelCase). `none` means no canonical redirect.
 *
 * This is the request contract only (`maran api` reads it against the command); the choices a picker
 * offers and their names come from the options answer itself.
 */
export type CanonicalHostWire = 'none' | 'www' | 'apex'

/** How a redirect rule's path is compared with the request path, mirroring `RedirectMatch`. */
export type RedirectMatchWire = 'exact' | 'prefix'

/** The HTTP status a redirect answers with, mirroring `RedirectStatus`. */
export type RedirectStatusWire = 'permanent' | 'temporary'

/** Whether a site's listed networks are the only ones allowed or the ones refused, mirroring `IpAccessMode`. */
export type IpAccessModeWire = 'allowList' | 'denyList'

/** One redirect rule of a site, in both directions on the wire. */
export interface SiteRedirect {
  /** The path the rule applies to; starts with `/`, and ends in `/` for a prefix rule. */
  path: string
  /** How `path` is compared with the request path. */
  match: RedirectMatchWire
  /** Where requests are sent: an absolute http(s) URL or a path on the same site. */
  target: string
  /** The HTTP status the redirect answers with. */
  status: RedirectStatusWire
}

/** A site's request limit, in both directions on the wire (`SiteRequestRateDto`). */
export interface SiteRequestRate {
  /** How many requests per second one visitor address may make. */
  perSecond: number
  /** How many requests above the rate one address may send at once before being refused. */
  burst: number
}

/** The bounds the options screen must respect, as the server states them (`SiteOptionsLimitsDto`). */
export interface SiteOptionsLimits {
  /** The most redirect rules one site may carry. */
  maximumRedirects: number
  /** The most basic-authentication users one site may carry. */
  maximumBasicAuthUsers: number
  /** The most IP rules one site may carry. */
  maximumIpRules: number
  /** The smallest request body limit that may be set, in MiB. */
  minimumBodySizeMib: number
  /** The largest request body limit that may be set, in MiB. */
  maximumBodySizeMib: number
  /** The largest request body limit the site's plan allows, in MiB, or `null` when the plan does not cap this site. */
  planMaximumBodySizeMib: number | null
  /** The smallest static-file cache lifetime that may be set, in days. */
  minimumCacheDays: number
  /** The largest static-file cache lifetime that may be set, in days. */
  maximumCacheDays: number
  /** The shortest basic-authentication password, in characters. */
  minimumPasswordLength: number
  /** The smallest request rate that may be set, in requests per second per visitor address. */
  minimumRequestsPerSecond: number
  /** The largest request rate that may be set, in requests per second per visitor address. */
  maximumRequestsPerSecond: number
  /** The smallest request burst that may be set. */
  minimumRequestBurst: number
  /** The largest request burst that may be set. */
  maximumRequestBurst: number
}

/** The closed sets the options screen selects from, labelled by the panel in the caller's language. */
export interface SiteOptionsChoices {
  /** The canonical-host choices, "none" included. */
  canonicalHosts: SiteChoice[]
  /** How a redirect rule's path may be compared. */
  redirectMatches: SiteChoice[]
  /** The statuses a redirect may answer with. */
  redirectStatuses: SiteChoice[]
  /** Whether the IP rules allow or refuse the listed networks. */
  ipAccessModes: SiteChoice[]
}

/**
 * One site's options, mirroring the backend's `SiteOptionsDto`, with the bounds and choices the
 * screen needs. Basic-authentication users are names only: the panel never returns a credential.
 */
export interface SiteOptions {
  /** The site the options belong to. */
  siteId: string
  /** Which of the apex / "www." pair is canonical. */
  canonicalHost: CanonicalHostWire
  /** The largest request body in MiB, or `null` for the web server's default. */
  clientMaxBodySizeMib: number | null
  /** How many days browsers may cache static files, or `null` for no cache headers. */
  staticCacheDays: number | null
  /** The redirect rules, in the order they are rendered. */
  redirects: SiteRedirect[]
  /** The names of the basic-authentication users; empty means no authentication. */
  basicAuthUsers: string[]
  /** Whether `ipRules` allows or refuses; `null` exactly when there are no rules. */
  ipAccessMode: IpAccessModeWire | null
  /** The addresses or CIDR ranges the access mode applies to. */
  ipRules: string[]
  /** The request limit, or `null` when the site is not limited. */
  requestRate: SiteRequestRate | null
  /** The bounds a replacement must respect. */
  limits: SiteOptionsLimits
  /** The closed sets a replacement selects from. */
  choices: SiteOptionsChoices
}

/** One basic-authentication user as a replacement states it. */
export interface SiteBasicAuthUserInput {
  /** The login name. */
  name: string
  /** The new password, or `null` to keep the one the user already has. Never read back from the server. */
  password: string | null
}

/**
 * The body of `PUT /api/v1/sites/{id}/options`, mirroring the backend's `ReplaceSiteOptionsCommand`.
 * It REPLACES all of a site's options: anything not stated is removed.
 */
export interface ReplaceSiteOptionsRequest {
  /** Which of the apex / "www." pair is canonical. */
  canonicalHost: CanonicalHostWire
  /** The largest request body in MiB, or `null` for the web server's default. */
  clientMaxBodySizeMib: number | null
  /** How many days browsers may cache static files, or `null` for no cache headers. */
  staticCacheDays: number | null
  /** The redirect rules, in the order they are rendered. */
  redirects: SiteRedirect[]
  /** The users allowed in; empty for no authentication. */
  basicAuthUsers: SiteBasicAuthUserInput[]
  /** Whether `ipRules` allows or refuses; `null` exactly when there are no rules. */
  ipAccessMode: IpAccessModeWire | null
  /** The addresses or CIDR ranges the access mode applies to. */
  ipRules: string[]
  /** The request limit, or `null` for no limit. */
  requestRate: SiteRequestRate | null
}

/**
 * The calls the site options endpoints support. Declared as an interface so the store can be tested
 * against a stand-in; called from the store only, never from a component (rules/vue.md).
 */
export interface SiteOptionsApi {
  /**
   * Reads one site's options. A site belonging to somebody else answers 404, never 403.
   * @param siteId The site to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The options with their bounds and choices.
   */
  get: (siteId: string, signal?: AbortSignal) => Promise<SiteOptions>

  /**
   * Replaces all of a site's options.
   * @param siteId The site to change.
   * @param request The complete new set.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The options as the panel stored them.
   */
  replace: (siteId: string, request: ReplaceSiteOptionsRequest, signal?: AbortSignal) => Promise<SiteOptions>
}

/** The sections of the options screen a refusal (or a malformed number) can be shown under. */
export type SiteOptionsSection =
  | 'canonicalHost'
  | 'uploadSize'
  | 'cache'
  | 'redirects'
  | 'basicAuth'
  | 'ipRules'
  | 'requestRate'
  | 'requestBurst'

/**
 * One row of the basic-auth editor: the form's own model of a user, apart from the wire shape
 * (`SiteBasicAuthUserInput`) it is turned into on save.
 */
export interface BasicAuthRow {
  /** The login name. A user that already exists on the site cannot be renamed, only removed and added again. */
  name: string
  /** The password typed for this row; empty for an existing user means "keep the one they have". */
  password: string
  /** Whether the user is already on the site, so an empty password is acceptable. */
  existing: boolean
}
