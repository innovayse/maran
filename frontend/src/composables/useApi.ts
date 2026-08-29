import { useLocaleStore } from '../stores/locale'
import type { ProblemDetails } from '../types/api'

/**
 * Single low-level HTTP client composable for the panel API. Requests are
 * relative to the app origin; the dev server proxies `/health` and `/api`
 * to the backend (see `vite.config.ts`). Every feature-specific API
 * composable (`composables/apis/use<Feature>Api.ts`) builds on this one —
 * nothing else in the app is allowed to call `fetch` directly (rules/vue.md).
 */

/**
 * Error thrown by {@link useApi}'s `get` when the server responds with a
 * non-2xx status. Carries the backend's own already-localized `title`/
 * `detail` text as `message` (rules/vue.md: "the backend owns their text")
 * so callers render it as-is instead of mapping `code` through a frontend
 * i18n key. `code` remains available for behavior decisions only (retry,
 * redirect, field highlight).
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

/** Public surface of the low-level API client returned by {@link useApi}. */
interface ApiClient {
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

/**
 * Builds the low-level API client: base URL resolution (the app origin),
 * default headers (including `Accept-Language`, so the backend can localize
 * error text and any other language-sensitive response), RFC 7807 error
 * decoding, and request cancellation via `AbortSignal`. There is exactly
 * one of this composable in the app; feature API composables call it, and
 * nothing else does.
 * @returns The {@link ApiClient} with a single `get` method.
 */
export const useApi = (): ApiClient => {
  // The locale store is the single source of truth for the interface language;
  // resolving it here keeps every request's Accept-Language in step with the UI.
  const localeStore = useLocaleStore()

  /**
   * Performs a GET request and returns the parsed JSON body of type `T`.
   * @param path Request path, relative to the app origin.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  /**
   * Throws an {@link ApiError} decoded from a non-2xx response's RFC 7807 body.
   * @param response The failed fetch response.
   * @returns Never returns; always throws.
   */
  const throwApiError = async (response: Response): Promise<never> => {
    // The body may not be valid JSON (e.g. an upstream proxy error page),
    // so fall back to an empty object rather than letting a parse error
    // mask the original HTTP failure.
    const problem = (await response.json().catch(() => ({}))) as ProblemDetails
    throw new ApiError(
      response.status,
      problem.code ?? 'unknown',
      problem.detail ?? problem.title ?? response.statusText,
    )
  }

  /**
   * Performs a GET request and returns the parsed JSON body of type `T`.
   * @param path Request path, relative to the app origin.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const get = async <T>(path: string, signal?: AbortSignal): Promise<T> => {
    const response = await fetch(path, {
      headers: {
        Accept: 'application/json',
        // Lets the backend pick which language to render `title`/`detail` in,
        // since the frontend never translates server-owned error text. Taken
        // from the locale store so server messages always match the interface
        // language rather than the browser's independent preference.
        'Accept-Language': localeStore.acceptLanguageHeader,
      },
      signal,
    })
    if (!response.ok) {
      await throwApiError(response)
    }
    return (await response.json()) as T
  }

  /**
   * Performs a POST request with a JSON body and returns the parsed JSON response body of type `T`.
   * @param path Request path, relative to the app origin.
   * @param body The request payload, serialized as JSON.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const post = async <T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> => {
    const response = await fetch(path, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
        // See `get` above: keeps server-produced error text in step with the interface language.
        'Accept-Language': localeStore.acceptLanguageHeader,
      },
      body: JSON.stringify(body),
      signal,
    })
    if (!response.ok) {
      await throwApiError(response)
    }
    return (await response.json()) as T
  }

  return { get, post }
}
