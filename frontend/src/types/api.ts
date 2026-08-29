/**
 * Shape of an RFC 7807 problem+json payload, as far as the low-level API
 * client (`src/composables/useApi.ts`) cares. The backend renders `title`
 * and `detail` already localized from its own resources (rules/vue.md:
 * "the backend owns their text") — the frontend surfaces them as-is and
 * never maps them through a frontend i18n key.
 */
export interface ProblemDetails {
  /**
   * Machine-stable problem code. Used for behavior only (retry/redirect/
   * field highlight), never as a text lookup key.
   */
  code?: string
  /** Backend-localized short summary of the problem. */
  title?: string
  /**
   * Backend-localized human-readable explanation, preferred over `title`
   * when present.
   */
  detail?: string
}
