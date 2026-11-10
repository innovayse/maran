import {
  API_METHODS,
  API_PATH_PREFIX,
  MAX_API_BODY_BYTES,
  MAX_PATH_LENGTH,
} from '../constants/bridgeLimits'
import { byteLength } from './byteLength'

/**
 * Checks an `api` request against the limits the shell enforces, before anything is sent.
 *
 * The prefix check is advice, exactly as in the shell: the server decides which endpoints the module
 * may reach, so passing this check grants nothing.
 *
 * @param method The HTTP method asked for.
 * @param path The API path asked for.
 * @param body The JSON body, if any.
 * @returns The serialised body (or `undefined` when there is none), or the reason the request is refused.
 */
export const checkApiRequest = (
  method: string,
  path: string,
  body: unknown,
): { ok: true; serialized: string | undefined } | { ok: false; reason: string } => {
  if (!API_METHODS.includes(method)) {
    return { ok: false, reason: `The method ${method} is not one the shell performs.` }
  }

  if (
    path.length > MAX_PATH_LENGTH ||
    !path.startsWith(API_PATH_PREFIX) ||
    path.includes('..') ||
    path.includes('//') ||
    path.includes('\\') ||
    path.includes('#')
  ) {
    return { ok: false, reason: `The path must be a plain path under ${API_PATH_PREFIX}.` }
  }

  if (body === undefined) {
    return { ok: true, serialized: undefined }
  }

  let serialized: string | undefined
  try {
    serialized = JSON.stringify(body)
  } catch {
    // A circular or otherwise unserialisable body can never reach the wire as JSON.
    return { ok: false, reason: 'The body is not JSON serialisable.' }
  }

  if (serialized === undefined) {
    return { ok: false, reason: 'The body is not JSON serialisable.' }
  }

  if (byteLength(serialized) > MAX_API_BODY_BYTES) {
    return { ok: false, reason: 'The body is larger than 1 MiB.' }
  }

  return { ok: true, serialized }
}
