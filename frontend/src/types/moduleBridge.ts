import type { ComputedRef, Ref } from 'vue'

/**
 * The shell's side of the packaged-module bridge, version 1: the messages a module's frame may
 * send, the messages the shell answers with, the limits both are held to, and the state a mounted
 * frame is in.
 *
 * A packaged module's screen runs in an iframe with an opaque origin (`sandbox="allow-scripts"`,
 * never `allow-same-origin`), so it shares no cookie, storage or script scope with the panel. The
 * only door is a `MessageChannel` the shell opens after a nonce-checked handshake. Everything that
 * crosses it is described here and checked by hand-written validators
 * (`utils/moduleBridgeMessages.ts`): a frame is third-party code and its messages are hostile input.
 */

/** The protocol version every message carries in `v`. */
export const MODULE_BRIDGE_VERSION = 1

/** Largest serialised size of any message except `api`, in bytes (64 KiB). */
export const MODULE_BRIDGE_MAX_MESSAGE_BYTES = 64 * 1024

/** Largest JSON body an `api` message may carry, in bytes (1 MiB). */
export const MODULE_BRIDGE_MAX_API_BODY_BYTES = 1024 * 1024

/** Largest serialised size of an `api` message, in bytes: the body limit plus 4 KiB of envelope. */
export const MODULE_BRIDGE_MAX_API_MESSAGE_BYTES = MODULE_BRIDGE_MAX_API_BODY_BYTES + 4 * 1024

/** How many `api` messages one mounted frame may send within {@link MODULE_BRIDGE_API_WINDOW_MS}. */
export const MODULE_BRIDGE_API_RATE_LIMIT = 30

/** The length of the sliding window the `api` rate limit counts in, in milliseconds. */
export const MODULE_BRIDGE_API_WINDOW_MS = 1000

/** How many violations a mounted frame may commit before the shell unmounts it. */
export const MODULE_BRIDGE_VIOLATION_LIMIT = 3

/** How long the shell waits for an accepted `ready` before giving up on the frame, in milliseconds. */
export const MODULE_BRIDGE_READY_TIMEOUT_MS = 10_000

/** Shortest height the shell gives a frame, in CSS pixels. */
export const MODULE_FRAME_MIN_HEIGHT = 120

/** Tallest height the shell gives a frame, in CSS pixels. */
export const MODULE_FRAME_MAX_HEIGHT = 4000

/** The height a frame has until it reports its own, in CSS pixels. */
export const MODULE_FRAME_INITIAL_HEIGHT = 320

/** How many random bytes a handshake nonce is made of; it is written as twice as many hex characters. */
export const MODULE_BRIDGE_NONCE_BYTES = 16

/** Longest toast text a frame may ask the shell to show, in characters. */
export const MODULE_BRIDGE_MAX_TOAST_LENGTH = 200

/** Longest `api` request id, in characters. */
export const MODULE_BRIDGE_MAX_REQUEST_ID_LENGTH = 64

/** The only prefix an `api` path may start with; the server decides scope, this is advice. */
export const MODULE_BRIDGE_API_PREFIX = '/api/v1/'

/** The HTTP methods an `api` message may name. */
export const MODULE_BRIDGE_API_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] as const

/** One of {@link MODULE_BRIDGE_API_METHODS}. */
export type ModuleApiMethod = (typeof MODULE_BRIDGE_API_METHODS)[number]

/** The colour scheme the shell tells a frame to draw in. */
export type ModuleBridgeTheme = 'dark' | 'light'

/** Frame to shell, on the window, once: the frame announces itself with the nonce from its address. */
export interface ModuleReadyMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'ready'
  /** The nonce read from the frame's own `location.hash`. */
  nonce: string
}

/** Frame to shell: the content height changed. */
export interface ModuleResizeMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'resize'
  /** The wanted height in CSS pixels; the shell clamps it. */
  height: number
}

/** Frame to shell: show another of the module's own screens. */
export interface ModuleNavigateMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'navigate'
  /** A module-relative path; accepted only when it is one of the module's declared routes. */
  path: string
}

/** Frame to shell: perform an API call with the shell's own session. */
export interface ModuleApiMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'api'
  /** Correlates the answer; 1 to 64 characters of `A-Za-z0-9_-`. */
  id: string
  /** The HTTP method. */
  method: ModuleApiMethod
  /** The request path, starting with `/api/v1/`. */
  path: string
  /** The JSON body, when the method has one; at most 1 MiB serialised. */
  body?: unknown
}

/** Frame to shell: show a short plain-text notice. */
export interface ModuleToastMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'toast'
  /** The text, 1 to 200 characters, shown as text and never as markup. */
  text: string
}

/** Every message a frame may send over the port. */
export type ModuleFrameMessage =
  | ModuleResizeMessage
  | ModuleNavigateMessage
  | ModuleApiMessage
  | ModuleToastMessage

/** Shell to frame, in the transfer of the port: everything the frame is told about its surroundings. */
export interface ModuleInitMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'init'
  /** The interface language. */
  locale: string
  /** The colour scheme. */
  theme: ModuleBridgeTheme
  /** The signed-in user's role name (`admin`, `reseller`, `customer`); never an identity. */
  roleName: string
  /** The module-relative path of the screen to draw. */
  route: string
}

/** Shell to frame: the answer to an `api` message. */
export interface ModuleApiResultMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'api-result'
  /** The id of the request this answers. */
  id: string
  /** The HTTP status, or 0 when the panel could not be reached. */
  status: number
  /** The parsed JSON body (a problem document on a refusal), or `null` when there was none. */
  body: unknown
}

/** Shell to frame: the interface language changed. */
export interface ModuleLocaleMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'locale'
  /** The new interface language. */
  locale: string
}

/** Shell to frame: the colour scheme changed. */
export interface ModuleThemeMessage {
  /** Protocol version. */
  v: typeof MODULE_BRIDGE_VERSION
  /** Discriminator. */
  type: 'theme'
  /** The new colour scheme. */
  theme: ModuleBridgeTheme
}

/** Every message the shell sends into a frame once the port exists. */
export type ModuleShellMessage = ModuleApiResultMessage | ModuleLocaleMessage | ModuleThemeMessage

/**
 * Why a message was refused. Each one counts as a violation; the third unmounts the frame.
 */
export type ModuleViolation =
  /** Not an object, wrong `v`, a field of the wrong type or outside its limits, or not serialisable. */
  | 'malformed'
  /** A well-formed object whose `type` the protocol does not know (or may not use at this point). */
  | 'unknown-type'
  /** Larger than the limit of its kind. */
  | 'oversized'
  /** More `api` messages than the rate limit allows. */
  | 'rate'
  /** A `navigate` to a path the module did not declare. */
  | 'out-of-route'
  /** A `ready` after one was accepted. */
  | 'second-ready'
  /** A `ready` whose nonce is not the one minted for this mount. */
  | 'bad-nonce'

/** Why a mounted frame stopped being shown. */
export type ModuleFailureReason =
  /** No accepted `ready` within {@link MODULE_BRIDGE_READY_TIMEOUT_MS}. */
  | 'timeout'
  /** The iframe reported a load error. */
  | 'load-error'
  /** {@link MODULE_BRIDGE_VIOLATION_LIMIT} violations. */
  | 'violations'

/** What a mounted frame is doing. */
export type ModuleMountState =
  /** The document is loading or the handshake is not finished. */
  | { state: 'loading' }
  /** The handshake finished; the module is running. */
  | { state: 'ready' }
  /** The frame was dropped; the screen offers a retry. */
  | { state: 'unavailable'; reason: ModuleFailureReason }

/** The outcome of checking one incoming message. */
export type ModuleMessageCheck =
  | { ok: true; message: ModuleFrameMessage }
  | { ok: false; violation: ModuleViolation }

/** The outcome of checking the handshake message. */
export type ModuleReadyCheck =
  | { ok: true; message: ModuleReadyMessage }
  | { ok: false; violation: ModuleViolation }

/** What the shell hands the store to perform on a frame's behalf. */
export interface ModuleApiRequest {
  /** The HTTP method. */
  method: ModuleApiMethod
  /** The request path, already checked to start with `/api/v1/`. */
  path: string
  /** The JSON body, when there is one. */
  body?: unknown
}

/** What the store hands back for a relayed call: the status and the parsed body, never headers. */
export interface ModuleApiResult {
  /** The HTTP status, or 0 when the panel could not be reached. */
  status: number
  /** The parsed JSON body (a problem document on a refusal), or `null` when there was none. */
  body: unknown
}

/** A failure the shell recorded for a module, for the operator's diagnosis. */
export interface ModuleFailureRecord {
  /** Why the frame was dropped. */
  reason: ModuleFailureReason
  /** The violations that led to it, in the order they happened; empty for a timeout or load error. */
  violations: ModuleViolation[]
}

/** What the screen asks of a bridge session. */
export interface ModuleBridgeOptions {
  /** The module's machine name; the server-enforced scope of its API calls. */
  moduleId: string
  /** The nonce minted for this mount. */
  nonce: string
  /** The module-relative paths of the module's declared routes. */
  routePaths: readonly string[]
  /** Answers the frame's window, or `null` before the iframe exists. */
  frameWindow: () => Window | null
  /** What `init` tells the frame, read at the moment of the handshake. */
  init: () => Omit<ModuleInitMessage, 'v' | 'type'>
  /** Performs a call on the frame's behalf; must resolve, never reject. */
  relay: (request: ModuleApiRequest) => Promise<ModuleApiResult>
  /** Called with the clamped height of a valid `resize`. */
  onResize: (height: number) => void
  /** Called with the path of a valid `navigate`. */
  onNavigate: (path: string) => void
  /** Called with the text of a valid `toast`. */
  onToast: (text: string) => void
  /** Called when the frame is dropped; the bridge is already closed. */
  onUnavailable: (failure: ModuleFailureRecord) => void
  /** Called when the handshake was accepted. */
  onReady: () => void
}

/** A running bridge session, owned by the frame component. */
export interface ModuleBridge {
  /** What the mount is doing. */
  state: Ref<ModuleMountState>
  /** How many violations have been counted. */
  violationCount: ComputedRef<number>
  /**
   * Starts listening and arms the handshake timer.
   * @returns Nothing.
   */
  open: () => void
  /**
   * Records an iframe load error and drops the frame.
   * @returns Nothing.
   */
  fail: () => void
  /**
   * Tells the frame the interface language changed; ignored before the handshake.
   * @param locale The new language.
   * @returns Nothing.
   */
  sendLocale: (locale: string) => void
  /**
   * Tells the frame the colour scheme changed; ignored before the handshake.
   * @param theme The new scheme.
   * @returns Nothing.
   */
  sendTheme: (theme: ModuleBridgeTheme) => void
  /**
   * Stops listening, closes the port and cancels the timer. Safe to call twice.
   * @returns Nothing.
   */
  close: () => void
}

/** What the sandboxed frame component exposes to the shell that mounts it. */
export interface SandboxFrameHandle {
  /** The `<iframe>` element, or `null` while it is not mounted. */
  element: HTMLIFrameElement | null
}
