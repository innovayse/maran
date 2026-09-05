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

/** How one request is described to the low-level client's internal `request` helper. */
export interface RequestOptions {
  /** HTTP method. */
  method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  /** The payload to serialize as JSON, or `undefined` for a body-less request. */
  body?: unknown
  /** Optional abort signal to cancel the in-flight request. */
  signal?: AbortSignal
  /**
   * Whether a 401 should renew the access token and replay the request. Defaults
   * to true. Sign-in calls set it to false: a wrong password is not a stale token,
   * and retrying would spend a refresh and hide the real answer.
   */
  retryOnUnauthorized?: boolean
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
  post: <T>(
    path: string,
    body?: unknown,
    signal?: AbortSignal,
    retryOnUnauthorized?: boolean,
  ) => Promise<T>

  /**
   * Performs a PUT request, replacing a resource wholesale.
   * @param path Request path, relative to the app origin (proxied in dev).
   * @param body The complete replacement, serialized as JSON.
   * @param signal Optional abort signal to cancel the request.
   * @returns The parsed JSON response body.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  put: <T>(path: string, body?: unknown, signal?: AbortSignal) => Promise<T>

  /**
   * Performs a PATCH request, changing part of a resource.
   * @param path Request path, relative to the app origin (proxied in dev).
   * @param body The change, serialized as JSON.
   * @param signal Optional abort signal to cancel the request.
   * @returns The parsed JSON response body.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  patch: <T>(path: string, body?: unknown, signal?: AbortSignal) => Promise<T>

  /**
   * Performs a DELETE request.
   * @param path Request path, relative to the app origin (proxied in dev).
   * @param signal Optional abort signal to cancel the request.
   * @returns The parsed JSON response body.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  delete: <T>(path: string, signal?: AbortSignal) => Promise<T>

  /**
   * Opens a Server-Sent Events stream and delivers each decoded frame to `onEvent`.
   *
   * Resolves when the server closes the stream or when `signal` is aborted, and releases the
   * connection either way — an abandoned stream that keeps a reader open is a leaked socket the
   * browser will not reclaim. `EventSource` is deliberately not used: it cannot carry the
   * `Authorization` header or the CSRF header every panel request needs.
   * @param path Request path, relative to the app origin (proxied in dev).
   * @param onEvent Called once per decoded frame, in the order the server sent them.
   * @param signal Abort signal that closes the stream; required, because a stream nobody can
   * stop is a stream that outlives the screen that opened it.
   * @returns Resolves once the stream has closed.
   * @throws {ApiError} When the stream could not be opened.
   */
  stream: (
    path: string,
    onEvent: (event: ServerSentEvent) => void,
    signal: AbortSignal,
  ) => Promise<void>
}

/**
 * One decoded Server-Sent Event, as the low-level client hands it to a caller.
 *
 * The panel streams live data over SSE (spec §17). The two fields are the only parts of the
 * wire format anything in the SPA needs: which kind of event this is, and its payload as sent.
 */
export interface ServerSentEvent {
  /** The event's name, or `'message'` when the frame carried no `event:` field, per the SSE spec. */
  name: string
  /** The frame's `data:` payload, with multiple data lines joined by newlines as the SSE spec requires. */
  data: string
}
