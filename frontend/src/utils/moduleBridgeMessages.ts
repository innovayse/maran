import {
  MODULE_BRIDGE_API_METHODS,
  MODULE_BRIDGE_API_PREFIX,
  MODULE_BRIDGE_MAX_API_BODY_BYTES,
  MODULE_BRIDGE_MAX_API_MESSAGE_BYTES,
  MODULE_BRIDGE_MAX_MESSAGE_BYTES,
  MODULE_BRIDGE_MAX_REQUEST_ID_LENGTH,
  MODULE_BRIDGE_MAX_TOAST_LENGTH,
  MODULE_BRIDGE_VERSION,
  MODULE_FRAME_MAX_HEIGHT,
  MODULE_FRAME_MIN_HEIGHT,
  type ModuleApiMessage,
  type ModuleApiMethod,
  type ModuleMessageCheck,
  type ModuleReadyCheck,
} from '../types/moduleBridge'

/**
 * Hand-written validators for the packaged-module bridge (version 1). A frame is third-party code,
 * so every message it sends is hostile input: these functions accept exactly the shapes of
 * `types/moduleBridge.ts` and name the violation for everything else. They are pure (no DOM, no
 * Vue), so the end-to-end suite exercises them directly with hostile samples.
 */

/** The characters an `api` request id may be made of. */
const REQUEST_ID_PATTERN = /^[A-Za-z0-9_-]+$/

/** The characters an `api` path may be made of: URL path and query characters, no fragment, no space, no backslash. */
const API_PATH_PATTERN = /^[A-Za-z0-9\-._~!$&'()*+,;=:@%/?]+$/

/** An encoded dot, slash or backslash: the spellings a traversal hides behind. */
const ENCODED_TRAVERSAL_PATTERN = /%(2e|2f|5c)/i

/** The longest `api` path accepted, in characters. */
const MAX_API_PATH_LENGTH = 2000

/** The longest nonce a `ready` may carry; a real one is 32 characters. */
const MAX_NONCE_LENGTH = 128

/** Encodes text to UTF-8 so a size is counted in bytes, as the limits are worded. */
const encoder = new TextEncoder()

/**
 * Answers whether a value is a plain object (not null, not an array).
 * @param value Anything.
 * @returns True for a non-null, non-array object.
 */
const isRecord = (value: unknown): value is Record<string, unknown> => {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * Reads the `type` of a message without trusting its shape.
 * @param data The `data` of a message event.
 * @returns The `type` when the message is an object carrying a string one, otherwise `null`.
 */
export const messageTypeOf = (data: unknown): string | null => {
  return isRecord(data) && typeof data.type === 'string' ? data.type : null
}

/**
 * Serialises a value to JSON without letting a hostile value throw.
 * @param value Anything a structured clone can carry.
 * @returns The JSON text, or `null` when the value cannot be serialised (a cycle, a BigInt, `undefined`).
 */
const serialise = (value: unknown): string | null => {
  try {
    return JSON.stringify(value) ?? null
  } catch {
    return null
  }
}

/**
 * The size of a value once serialised, in UTF-8 bytes.
 * @param value Anything.
 * @returns The byte count, or `null` when the value cannot be serialised.
 */
export const serialisedSize = (value: unknown): number | null => {
  const json = serialise(value)
  return json === null ? null : encoder.encode(json).length
}

/**
 * Answers whether an `api` path is acceptable to relay. This is advice: the server decides which
 * endpoints a module may reach (`X-Maran-Frame`), and this check only refuses what could never be
 * a request to the panel's own API — another host, a traversal, an encoded separator.
 * @param path The path the frame named.
 * @returns True when it starts with `/api/v1/` and contains nothing that could leave it.
 */
export const isApiPathAllowed = (path: string): boolean => {
  return (
    path.length <= MAX_API_PATH_LENGTH &&
    path.startsWith(MODULE_BRIDGE_API_PREFIX) &&
    API_PATH_PATTERN.test(path) &&
    !path.includes('..') &&
    !path.includes('//') &&
    !ENCODED_TRAVERSAL_PATTERN.test(path)
  )
}

/**
 * Clamps a requested frame height to the range the shell allows.
 * @param height The height the frame asked for, in CSS pixels.
 * @returns A whole number between the minimum and maximum frame height.
 */
export const clampFrameHeight = (height: number): number => {
  return Math.min(MODULE_FRAME_MAX_HEIGHT, Math.max(MODULE_FRAME_MIN_HEIGHT, Math.round(height)))
}

/**
 * Checks the handshake message a frame posts to the window.
 * @param data The `data` of the message event.
 * @returns The message, or the violation it commits. The nonce is only checked for shape here; the
 * caller compares it with the one it minted.
 */
export const checkReadyMessage = (data: unknown): ModuleReadyCheck => {
  if (!isRecord(data) || data.v !== MODULE_BRIDGE_VERSION) {
    return { ok: false, violation: 'malformed' }
  }
  if (data.type !== 'ready') {
    return { ok: false, violation: typeof data.type === 'string' ? 'unknown-type' : 'malformed' }
  }
  if (typeof data.nonce !== 'string' || data.nonce.length === 0 || data.nonce.length > MAX_NONCE_LENGTH) {
    return { ok: false, violation: 'malformed' }
  }
  return { ok: true, message: { v: MODULE_BRIDGE_VERSION, type: 'ready', nonce: data.nonce } }
}

/**
 * Checks an `api` message's own fields (its size has been checked already).
 * @param data The message.
 * @returns The validated message, or the violation it commits.
 */
const checkApiMessage = (data: Record<string, unknown>): ModuleMessageCheck => {
  const { id, method, path, body } = data
  if (
    typeof id !== 'string' ||
    id.length === 0 ||
    id.length > MODULE_BRIDGE_MAX_REQUEST_ID_LENGTH ||
    !REQUEST_ID_PATTERN.test(id) ||
    typeof method !== 'string' ||
    !MODULE_BRIDGE_API_METHODS.includes(method as ModuleApiMethod) ||
    typeof path !== 'string' ||
    !isApiPathAllowed(path)
  ) {
    return { ok: false, violation: 'malformed' }
  }
  if (body !== undefined) {
    const size = serialisedSize(body)
    if (size === null) {
      return { ok: false, violation: 'malformed' }
    }
    if (size > MODULE_BRIDGE_MAX_API_BODY_BYTES) {
      return { ok: false, violation: 'oversized' }
    }
  }
  const message: ModuleApiMessage = {
    v: MODULE_BRIDGE_VERSION,
    type: 'api',
    id,
    method: method as ModuleApiMethod,
    path,
    ...(body === undefined ? {} : { body }),
  }
  return { ok: true, message }
}

/**
 * Checks one message that arrived over the port.
 * @param data The `data` of the message event.
 * @param routePaths The module-relative paths of the module's declared routes.
 * @returns The validated message, or the violation it commits.
 */
export const checkFrameMessage = (data: unknown, routePaths: readonly string[]): ModuleMessageCheck => {
  if (!isRecord(data) || data.v !== MODULE_BRIDGE_VERSION || typeof data.type !== 'string') {
    return { ok: false, violation: 'malformed' }
  }
  const size = serialisedSize(data)
  if (size === null) {
    return { ok: false, violation: 'malformed' }
  }
  const limit = data.type === 'api' ? MODULE_BRIDGE_MAX_API_MESSAGE_BYTES : MODULE_BRIDGE_MAX_MESSAGE_BYTES
  if (size > limit) {
    return { ok: false, violation: 'oversized' }
  }
  switch (data.type) {
    case 'resize':
      return typeof data.height === 'number' && Number.isInteger(data.height) && data.height >= 0
        ? { ok: true, message: { v: MODULE_BRIDGE_VERSION, type: 'resize', height: data.height } }
        : { ok: false, violation: 'malformed' }
    case 'navigate':
      if (typeof data.path !== 'string') {
        return { ok: false, violation: 'malformed' }
      }
      return routePaths.includes(data.path)
        ? { ok: true, message: { v: MODULE_BRIDGE_VERSION, type: 'navigate', path: data.path } }
        : { ok: false, violation: 'out-of-route' }
    case 'api':
      return checkApiMessage(data)
    case 'toast':
      return typeof data.text === 'string' && data.text.length > 0 && data.text.length <= MODULE_BRIDGE_MAX_TOAST_LENGTH
        ? { ok: true, message: { v: MODULE_BRIDGE_VERSION, type: 'toast', text: data.text } }
        : { ok: false, violation: 'malformed' }
    default:
      // Includes `ready` over the port and every shell-to-frame type: nothing else is a valid
      // frame message once the handshake is over.
      return { ok: false, violation: 'unknown-type' }
  }
}
