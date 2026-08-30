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

/** Public surface of the low-level API client returned by {@link useApi}. */
export interface ApiClient {
  /**
   * Performs a GET request and returns the parsed JSON body of type `T`.
   * @param path Request path, relative to the app origin (proxied in dev).
   * @param signal Optional abort signal to cancel the request.
   * @returns The parsed JSON response body.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  get: <T>(path: string, signal?: AbortSignal) => Promise<T>

  /**
   * Performs a POST request with a JSON body and returns the parsed JSON response body of type `T`.
   * @param path Request path, relative to the app origin (proxied in dev).
   * @param body The request payload, serialized as JSON.
   * @param signal Optional abort signal to cancel the request.
   * @returns The parsed JSON response body.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  post: <T>(path: string, body: unknown, signal?: AbortSignal) => Promise<T>
}
