import { useAuthStore } from '../stores/auth'
import { useLocaleStore } from '../stores/locale'
import { contentDispositionFileName } from '../utils/contentDispositionFileName'
import { decodeServerSentFrame } from '../utils/decodeServerSentFrame'
import type {
  ApiClient,
  ApiDownload,
  ApiRelayAnswer,
  ApiRelayOptions,
  ProblemDetails,
  RequestOptions,
  ServerSentEvent,
} from '../types/api'

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

/** The name a download is saved under when the response proposes none. */
const DEFAULT_DOWNLOAD_NAME = 'download'

/**
 * Problem members that are NOT feature extensions: RFC 7807's own five plus the two the backend
 * writes on every failure. Everything else in a problem body is an extension member and is handed
 * to callers on `ApiError.extensions`.
 */
const STANDARD_PROBLEM_MEMBERS: readonly string[] = [
  'type',
  'title',
  'status',
  'detail',
  'instance',
  'code',
  'correlationId',
]

/**
 * The media type a request's body goes out as: octet-stream for a raw upload, JSON for a payload,
 * none for a request without a body.
 * @param options The request as described to `request`.
 * @returns The `Content-Type` value, or null when there is no body.
 */
const contentTypeOf = (options: RequestOptions): string | null => {
  if (options.rawBody !== undefined) {
    return 'application/octet-stream'
  }

  return options.body === undefined ? null : 'application/json'
}

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
   * The problem's RFC 7807 extension members beyond the standard ones — the panel's
   * problem-extension convention delivers a failure's typed facts here (a partial restore's
   * counts, above all), each under its own key at the top level of the problem JSON. Values are
   * `unknown` on purpose: the feature that knows a member's shape narrows it with its own guard
   * (e.g. `src/utils/restorePartialCounts.ts`), and everything else ignores the map entirely.
   * Empty when the server sent none.
   */
  readonly extensions: Readonly<Record<string, unknown>>

  /**
   * Builds an error from a problem+json payload.
   * @param status HTTP status code of the failed response.
   * @param code Machine-stable problem code, or `'unknown'` if absent.
   * @param message Backend-localized message (`detail`, falling back to `title`, falling back to the HTTP status text).
   * @param extensions The problem's extension members beyond the standard ones; empty when none came.
   */
  constructor(
    status: number,
    code: string,
    message: string,
    extensions: Readonly<Record<string, unknown>> = {},
  ) {
    super(message)
    // Name the error explicitly: minifiers/transpilers can otherwise drop
    // the constructor name, which would break `error.name === 'ApiError'`
    // checks in logging/monitoring.
    this.name = 'ApiError'
    this.code = code
    this.status = status
    this.extensions = extensions
  }
}

/**
 * Builds the low-level API client: base URL resolution (the app origin),
 * default headers (including `Accept-Language`, so the backend can localize
 * error text, the bearer token, and the CSRF header), RFC 7807 error decoding,
 * request cancellation via `AbortSignal`, and one silent retry after renewing
 * an expired access token. There is exactly one of this composable in the app;
 * feature API composables call it, and nothing else does.
 * @returns The {@link ApiClient} with `get`, `post`, `put`, `patch`, `delete`, `download`, `upload`, `stream` and `relay`.
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

    // Everything beyond the standard members is a feature's typed extension data (the panel's
    // problem-extension convention); it travels on the error so a store can read it without this
    // client knowing any feature's shape. The plain-object check keeps a proxy's non-object JSON
    // body (a bare string or array still parses) from being walked as if it had members.
    const extensions: Record<string, unknown> = {}
    if (typeof problem === 'object' && problem !== null && !Array.isArray(problem)) {
      for (const [member, value] of Object.entries(problem)) {
        if (!STANDARD_PROBLEM_MEMBERS.includes(member)) {
          extensions[member] = value
        }
      }
    }

    throw new ApiError(
      response.status,
      problem.code ?? 'unknown',
      problem.detail ?? problem.title ?? response.statusText,
      extensions,
    )
  }

  /**
   * Builds the headers every request carries.
   * @param contentType The media type of the body, or null for a request without one.
   * @param accept The media types the caller will take; JSON unless a download asks otherwise.
   * @param extra Headers the caller declares; the headers this client owns win over them.
   * @returns The header map.
   */
  const buildHeaders = (
    contentType: string | null,
    accept: string,
    extra: Record<string, string> = {},
  ): Record<string, string> => {
    const headers: Record<string, string> = {
      // First, so that none of the headers the client owns below can be replaced by a caller's.
      ...extra,
      Accept: accept,
      // Lets the backend pick which language to render `title`/`detail` in,
      // since the frontend never translates server-owned error text.
      'Accept-Language': localeStore.acceptLanguageHeader,
      // A header a cross-site form cannot set. The backend requires it on every
      // cookie-bearing state change, which is the CSRF defence SameSite backs up.
      [CSRF_HEADER]: '1',
    }

    if (contentType !== null) {
      headers['Content-Type'] = contentType
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
   * @returns The raw response, known to be successful unless the options asked for failures to be passed on.
   * @throws {ApiError} When the response status is not in the 2xx range and failures are not passed on.
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
        headers: buildHeaders(contentTypeOf(options), options.accept ?? 'application/json', options.headers),
        body: options.rawBody ?? (options.body === undefined ? undefined : JSON.stringify(options.body)),
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

    if (!response.ok && options.passFailure !== true) {
      await throwApiError(response)
    }

    return response
  }

  /**
   * Reads a response body as `T`, tolerating an empty one.
   *
   * An endpoint that has nothing to return answers 204 No Content with no body; the caller types
   * it `<void>` (`api.post<void>(path)`) and receives `undefined`. Errors never reach this point:
   * `request` has already decoded any non-2xx response into an `ApiError`.
   * @param response The successful response.
   * @returns The parsed body, or `undefined` cast to `T` (`void` for a 204) when there was none.
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

  /**
   * Performs a POST request whose body is a file's raw bytes, for an upload.
   *
   * Sent with `fetch`, so no upload progress is reported (rules/vue.md allows no other client);
   * the replay after a token renewal re-sends the same `Blob`, which a file keeps readable.
   * @param path Request path, relative to the app origin.
   * @param body The file's bytes, sent unchanged as `application/octet-stream`.
   * @param signal Optional abort signal to cancel the upload.
   * @returns The parsed JSON response body, cast to `T`.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const upload = async <T>(path: string, body: Blob, signal?: AbortSignal): Promise<T> => {
    return readJson<T>(await request(path, { method: 'POST', rawBody: body, signal }))
  }

  /**
   * Performs a GET request for a file and returns it whole, with the name the panel proposed.
   *
   * It goes through the same `request` helper as every other call, so the bearer token, the CSRF
   * header, the one-shot token renewal and the RFC 7807 error decoding behave identically; a plain
   * link could carry none of them. The body is read into memory, which the export endpoints bound
   * with their row caps.
   * @param path Request path, relative to the app origin.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The file and its proposed name.
   * @throws {ApiError} When the response status is not in the 2xx range.
   */
  const download = async (path: string, signal?: AbortSignal): Promise<ApiDownload> => {
    const response = await request(path, { method: 'GET', signal, accept: '*/*' })
    return {
      blob: await response.blob(),
      fileName: contentDispositionFileName(response.headers.get('Content-Disposition')) ?? DEFAULT_DOWNLOAD_NAME,
    }
  }

  /**
   * Opens a Server-Sent Events stream and delivers each decoded frame to `onEvent`.
   *
   * `EventSource` is not used here on purpose: it sends neither the bearer token nor the CSRF
   * header, so it cannot talk to this panel's API at all. The stream therefore goes through the
   * same `request` helper as every other call, which means it also gets the same one-shot token
   * renewal and the same RFC 7807 error decoding.
   * @param path Request path, relative to the app origin.
   * @param onEvent Called once per decoded frame, in the order the server sent them.
   * @param signal Abort signal that closes the stream and releases its connection.
   * @returns Resolves once the stream has closed, whether by the server or by the abort.
   * @throws {ApiError} When the stream could not be opened.
   */
  const stream = async (
    path: string,
    onEvent: (event: ServerSentEvent) => void,
    signal: AbortSignal,
  ): Promise<void> => {
    const response = await request(path, { method: 'GET', signal })
    const body = response.body
    if (body === null) {
      // A 2xx with no body is not a stream; treating it as an empty one would look to the caller
      // like a log that simply ended, which is the silent truncation this design exists to avoid.
      throw new ApiError(response.status, 'unknown', response.statusText)
    }

    const reader = body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    try {
      for (;;) {
        const chunk = await reader.read()
        if (chunk.done) {
          break
        }

        buffer += decoder.decode(chunk.value, { stream: true })

        // Frames are separated by a blank line. Everything after the last separator is a partial
        // frame and stays in the buffer: a chunk boundary falls wherever TCP put it, not where
        // the protocol did, and emitting half a frame would hand the caller half a log line.
        let separator = buffer.indexOf('\n\n')
        while (separator !== -1) {
          const decoded = decodeServerSentFrame(buffer.slice(0, separator))
          buffer = buffer.slice(separator + 2)
          if (decoded !== null) {
            onEvent(decoded)
          }

          separator = buffer.indexOf('\n\n')
        }
      }
    } finally {
      // Always, including on an abort: an unreleased reader holds the connection open, and a
      // panel that opens a log tab per navigation would exhaust the browser's connection pool.
      reader.cancel().catch(() => {
        // The stream is already gone; there is nothing left to release and nothing to report.
      })
    }
  }

  /**
   * Performs a request for another party and returns the server's answer as data.
   *
   * The body is parsed leniently: an empty or non-JSON body becomes `null`, because the party the
   * answer goes to (a module's frame) is owed the status either way, and a proxy's HTML error page
   * is not a reason to withhold it.
   * @param path Request path, relative to the app origin.
   * @param options Method, optional JSON body and the extra headers to declare.
   * @returns The HTTP status and the parsed body.
   */
  const relay = async (path: string, options: ApiRelayOptions): Promise<ApiRelayAnswer> => {
    const response = await request(path, {
      method: options.method,
      body: options.body,
      headers: options.headers,
      passFailure: true,
    })
    const text = await response.text()
    let body: unknown = null
    if (text.length > 0) {
      try {
        body = JSON.parse(text)
      } catch {
        // Not JSON (a proxy's error page): the status still travels, the body is withheld.
        body = null
      }
    }
    return { status: response.status, body }
  }

  return { get, post, put, patch, delete: deleteResource, download, upload, stream, relay }
}
