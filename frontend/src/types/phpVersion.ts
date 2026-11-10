/**
 * Whether an installed PHP runtime is the host's default CLI PHP, as the backend's
 * `PhpDefaultStatus` reports it. `unknown` is a real answer — the agent did not establish the
 * default — and is not the same as `notDefault`.
 */
export type PhpDefaultStatus = 'default' | 'notDefault' | 'unknown'

/**
 * One PHP runtime installed on this server, mirroring the backend's `PhpVersionDto`
 * field-for-field.
 *
 * The set of versions is host state, not application knowledge: it is whatever the agent found
 * installed. The SPA therefore never carries a list of versions of its own — it renders what
 * `GET /api/v1/sites/php-versions` reports (rules/vue.md: "the frontend never invents domain
 * data").
 */
export interface PhpVersion {
  /** Two-component version as the packages name it, e.g. `8.3`. */
  version: string
  /**
   * Whether this version is the host's default CLI PHP. `unknown` and `notDefault` are different
   * answers and must not be conflated: "not known" is not "not the default", and the backend
   * deliberately keeps them apart.
   */
  defaultStatus: PhpDefaultStatus
  /** Where this version stands in its support lifecycle, as the panel's own table reads it. */
  lifecycle: PhpLifecycle
  /**
   * How many sites run on this version. `null` for a customer: the figure spans every account, so
   * the panel withholds it, and `null` is "not shown to you" and never "zero".
   */
  sitesUsing: number | null
}

/**
 * Where a PHP version stands in its support lifecycle, as the backend's `PhpSupportStatus`
 * reports it. `unknown` is a real answer — the panel's table has no row for the version — and is
 * never to be read as `supported`.
 */
export type PhpSupportStatus = 'supported' | 'securityOnly' | 'endOfLife' | 'unknown'

/**
 * The support lifecycle of one PHP version, mirroring the backend's `PhpLifecycleDto`
 * field-for-field. The panel reads it from a static table and the clock; the SPA only displays it.
 */
export interface PhpLifecycle {
  /** The phase the version is in. Picks the badge's tone and icon; never the badge's text. */
  status: PhpSupportStatus
  /** The phase as an operator reads it, already in their language; the badge's word, shown verbatim. */
  statusName: string
  /** The day security support ends (`YYYY-MM-DD`), or `null` when the panel does not know it. */
  endOfLife: string | null
  /** Whole days until {@link endOfLife}, or `null` when there is no date to count to. */
  daysLeft: number | null
}

/**
 * Whether a PHP extension can be switched on a server, mirroring the backend's
 * `ExtensionAvailability`. `unknown` is a real answer and is not the same as `available`.
 */
export type PhpExtensionAvailability = 'available' | 'disableNotSupported' | 'notPackaged' | 'unknown'

/**
 * One extension of a PHP version, mirroring the backend's `PhpExtensionDto` field-for-field. The
 * set of extensions is the panel's closed catalogue: the SPA lists what it receives.
 */
export interface PhpExtension {
  /** The catalogue name, e.g. `gd`; the only identifier a toggle request may name. */
  name: string
  /** The extension as an operator reads it, already in their language; shown verbatim. */
  label: string
  /** Whether the version's PHP loads the extension right now. */
  enabled: boolean
  /**
   * What can be done to it on this server, or `null` for a caller who may not change it (a
   * customer): then the list is read-only and says nothing about packages.
   */
  availability: PhpExtensionAvailability | null
}

/** Request body for `PUT /api/v1/sites/php-versions/{version}/extensions/{name}`. */
export interface SetPhpExtensionRequest {
  /** The state to leave the extension in. */
  enabled: boolean
}
