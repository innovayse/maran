import { useAuthStore } from '../stores/auth'
import { useLocaleStore } from '../stores/locale'
import type { ApiClient, ProblemDetails, RequestOptions } from '../types/api'

/**
 * Single low-level HTTP client composable for the panel API. Requests are
 * relative to the app origin; the dev server proxies `/health` and `/api`
 * to the backend (see `vite.config.ts`). Every feature-specific API
 * composable (`composables/apis/use<Feature>Api.ts`) builds on this one —
 * nothing else in the app is allowed to call `fetch` directly (rules/vue.md).
 */

/** The header the backend's CSRF middleware requires on every state-changing request. */
const CSRF_HEADER = 'X-Maran-Request'

/** The endpoint that renews a session; it must never be retried by the retry logic itself. */
const REFRESH_PATH = '/api/v1/auth/refresh'

/**
 * Error thrown by {@link useApi} when the server responds with a non-2xx
 * status. Carries the backend's own already-localized `title`/`detail` text as
 * `message` (rules/vue.md: "the backend owns their text") so callers render it
 * as-is instead of mapping `code` through a frontend i18n key. `code` remains
 * available for behavior decisions only (retry, redirect, field highlight).
 *
 * This stays a runtime class in `useApi.ts` rather than moving to
 * `src/types/` with the rest of this file's types: it is constructed with
 * `new` and checked with `instanceof` at runtime, so it cannot be imported
 * with `import type` the way a pure type/interface can (rules/vue.md's
 * types-folder rule targets type-only declarations).
 */
export class ApiError extends Error {
  /** Machine-stable problem code. Used for behavior only, never as a text lookup key. */
  readonly code: string

  /** HTTP status of the failed call. */
  readonly status: number

  /**
   * Builds an error from a problem+json payload.
   * @param status HTTP status code of the failed response.
   * @param code Machine-stable problem code, or `'unknown'` if absent.
   * @param message Backend-localized message (`detail`, falling back to `title`, falling back to the HTTP status text).
   */
  constructor(status: number, code: string, message: string) {
    super(message)
    // Name the error explicitly: minifiers/transpilers can otherwise drop
    // the constructor name, which would break `error.name === 'ApiError'`
    // checks in logging/monitoring.
    this.name = 'ApiError'
    this.code = code
    this.status = status
  }
}

/**
 * Builds the low-level API client: base URL resolution (the app origin),
 * default headers (including `Accept-Language`, so the backend can localize
 * error text, the bearer token, and the CSRF header), RFC 7807 error decoding,
 * request cancellation via `AbortSignal`, and one silent retry after renewing
 * an expired access token. There is exactly one of this composable in the app;
 * feature API composables call it, and nothing else does.
 * @returns The {@link ApiClient} with `get`, `post`, `put`, `patch` and `delete`.
 */
export const useApi = (): ApiClient => {
  // The locale store is the single source of truth for the interface language;
  // resolving it here keeps every request's Accept-Language in step with the UI.
  const localeStore = useLocaleStore()

  // Resolved lazily inside the request, not here: the auth store's own actions
  // go through this composable, and resolving it at build time would be a cycle.
  const authStore = useAuthStore()

  /**
   * Throws an {@link ApiError} decoded from a non-2xx response's RFC 7807 body.
   * @param response The failed fetch response.
   * @returns Never returns; always throws.
   */
  const throwApiError = async (response: Response): Promise<never> => {
    // The body may not be valid JSON (e.g. an upstream proxy error page),
    // so fall back to an empty object rather than letting a parse error
    // mask the original HTTP failure.
    const problem = (await response.json().catch(() => {
      return {}
    })) as ProblemDetails
    throw new ApiError(
      response.status,
      problem.code ?? 'unknown',
      problem.detail ?? problem.title ?? response.statusText,
    )
  }

  /**
   * Builds the headers every request carries.
   * @param hasBody Whether the request sends a JSON body.
   * @returns The header map.
   */
  const buildHeaders = (hasBody: boolean): Record<string, string> => {
    const headers: Record<string, string> = {
      Accept: 'application/json',
      // Lets the backend pick which language to render `title`/`detail` in,
      // since the frontend never translates server-owned error text.
      'Accept-Language': localeStore.acceptLanguageHeader,
      // A header a cross-site form cannot set. The backend requires it on every
      // cookie-bearing state change, which is the CSRF defence SameSite backs up.
      [CSRF_HEADER]: '1',
    }

    if (hasBody) {
      headers['Content-Type'] = 'application/json'
    }

    if (authStore.accessToken !== null) {
      headers.Authorization = `Bearer ${authStore.accessToken}`
    }

    return headers
  }

  /**
   * Performs one request, renewing the access token and replaying it once on a 401.
   * @param path Request path, relative to the app origin.
   * @param options Method, body and abort signal.
   * @returns The raw response, already known to be successful.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const request = async (path: string, options: RequestOptions): Promise<Response> => {
    /**
     * Sends the request as currently configured, headers rebuilt each time so a
     * replay after a renewal carries the NEW token rather than the expired one.
     * @returns The raw response.
     */
    const send = (): Promise<Response> => {
      return fetch(path, {
        method: options.method,
        headers: buildHeaders(options.body !== undefined),
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        // Sends the refresh cookie on the paths scoped to receive it.
        credentials: 'include',
        signal: options.signal,
      })
    }

    let response = await send()

    // An expired access token is the ordinary state of a reloaded page, not an
    // error worth showing: renew once and replay. `path !== REFRESH_PATH` stops
    // the renewal from renewing itself, and `retryOnUnauthorized` lets the auth
    // store's own sign-in calls opt out — a wrong password is not a stale token.
    if (
      response.status === 401 &&
      path !== REFRESH_PATH &&
      options.retryOnUnauthorized !== false &&
      (await authStore.renewAccessToken())
    ) {
      response = await send()
    }

    if (!response.ok) {
      await throwApiError(response)
    }

    return response
  }

  /**
   * Reads a response body as `T`, tolerating an empty one.
   * @param response The successful response.
   * @returns The parsed body, or `undefined` cast to `T` when there was none.
   */
  const readJson = async <T>(response: Response): Promise<T> => {
    const text = await response.text()
    return (text.length === 0 ? undefined : JSON.parse(text)) as T
  }

  /**
   * Performs a GET request and returns the parsed JSON body of type `T`.
   * @param path Request path, relative to the app origin.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const get = async <T>(path: string, signal?: AbortSignal): Promise<T> => {
    return readJson<T>(await request(path, { method: 'GET', signal }))
  }

  /**
   * Performs a POST request with a JSON body and returns the parsed JSON response body of type `T`.
   * @param path Request path, relative to the app origin.
   * @param body The request payload, serialized as JSON. Omit for a body-less POST.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @param retryOnUnauthorized Whether a 401 should renew the token and replay; false for sign-in calls.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const post = async <T>(
    path: string,
    body?: unknown,
    signal?: AbortSignal,
    retryOnUnauthorized?: boolean,
  ): Promise<T> => {
    return readJson<T>(await request(path, { method: 'POST', body, signal, retryOnUnauthorized }))
  }

  /**
   * Performs a PUT request, replacing a resource wholesale.
   * @param path Request path, relative to the app origin.
   * @param body The complete replacement, serialized as JSON.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const put = async <T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> => {
    return readJson<T>(await request(path, { method: 'PUT', body, signal }))
  }

  /**
   * Performs a PATCH request, changing part of a resource.
   * @param path Request path, relative to the app origin.
   * @param body The change, serialized as JSON.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const patch = async <T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> => {
    return readJson<T>(await request(path, { method: 'PATCH', body, signal }))
  }

  /**
   * Performs a DELETE request.
   *
   * Named `deleteResource` here and exposed as `delete`: `delete` is a reserved word
   * and cannot name a `const`, but it can name a property — so callers read
   * `api.delete(…)`, which is the method they are actually asking for.
   * @param path Request path, relative to the app origin.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const deleteResource = async <T>(path: string, signal?: AbortSignal): Promise<T> => {
    return readJson<T>(await request(path, { method: 'DELETE', signal }))
  }

  return { get, post, put, patch, delete: deleteResource }
}
