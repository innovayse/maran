/**
 * Whether an installed PHP runtime is the host's default CLI PHP, as the backend's
 * `PhpDefaultStatus` reports it. `unknown` is a real answer — the agent did not establish the
 * default — and is not the same as `notDefault`.
 */
export type PhpDefaultStatus = 'default' | 'notDefault' | 'unknown'

/**
 * One PHP version this server can install, mirroring the backend's `InstallablePhpVersionDto`
 * field-for-field.
 *
 * The set is whatever the agent reports the host's package repository can install; the SPA never
 * carries a list of versions of its own. The install picker offers the versions where `installed`
 * is `false`, and the backend validates the chosen one against the same set again.
 */
export interface InstallablePhpVersion {
  /** Two-component version as the packages name it, e.g. `8.3`. */
  version: string
  /** Whether the server already has this version installed. */
  installed: boolean
}

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
}
