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
   * Whether this version is the host's default CLI PHP, or `null` when the agent could not
   * establish it. Null and false are different answers and must not be conflated: "not known"
   * is not "not the default", and the backend deliberately keeps them apart.
   */
  isDefault: boolean | null
}
