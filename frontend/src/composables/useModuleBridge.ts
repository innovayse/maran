import { computed, ref, type Ref } from 'vue'
import { createRateLimiter } from '../utils/moduleBridgeRateLimit'
import { checkFrameMessage, checkReadyMessage, clampFrameHeight, messageTypeOf } from '../utils/moduleBridgeMessages'
import {
  MODULE_BRIDGE_API_RATE_LIMIT,
  MODULE_BRIDGE_API_WINDOW_MS,
  MODULE_BRIDGE_READY_TIMEOUT_MS,
  MODULE_BRIDGE_VERSION,
  MODULE_BRIDGE_VIOLATION_LIMIT,
  type ModuleApiMessage,
  type ModuleBridge,
  type ModuleBridgeOptions,
  type ModuleBridgeTheme,
  type ModuleFailureReason,
  type ModuleMountState,
  type ModuleShellMessage,
  type ModuleViolation,
} from '../types/moduleBridge'

/**
 * The shell's end of one packaged module's frame: the nonce-checked handshake, the
 * `MessageChannel` it opens, and the validation, counting and limiting of everything the frame
 * sends over it.
 *
 * The frame has an opaque origin, so its messages cannot be told apart by `event.origin`; the
 * handshake is accepted only from the iframe's own window (`event.source`), with the nonce this
 * mount minted, and only once. After that nothing travels on the window again: every message is
 * on the port, which only that frame holds. What the shell sends into the frame is exactly
 * `init`, `locale`, `theme` and `api-result` (never a token, a cookie, a CSRF value or a user).
 *
 * All the decisions that can be made without a browser are in `utils/moduleBridgeMessages.ts` and
 * `utils/moduleBridgeRateLimit.ts`; this composable only wires them to the window and the port.
 * @param options What the screen provides: the module, its nonce and routes, and the callbacks.
 * @returns The running {@link ModuleBridge}; call `open` once the iframe is in the document.
 */
export const useModuleBridge = (options: ModuleBridgeOptions): ModuleBridge => {
  /** What the mount is doing; the screen draws the frame or the unavailable panel from it. */
  const state: Ref<ModuleMountState> = ref({ state: 'loading' })

  /** The violations counted so far, in order. */
  const violations: Ref<ModuleViolation[]> = ref([])

  /** How many violations have been counted. */
  const violationCount = computed((): number => {
    return violations.value.length
  })

  /** The shell's end of the channel, once the handshake was accepted. */
  let port: MessagePort | null = null

  /** The handshake deadline. */
  let timer: ReturnType<typeof setTimeout> | null = null

  /** Whether a `ready` was accepted; a second one is a violation. */
  let accepted = false

  /** Whether the bridge was closed; a closed bridge ignores everything. */
  let closed = false

  /** Ends the window listener when the bridge closes, without the listener having to be named. */
  const lifetime = new AbortController()

  /** Counts `api` messages against the rate limit. */
  const allowApi = createRateLimiter(MODULE_BRIDGE_API_RATE_LIMIT, MODULE_BRIDGE_API_WINDOW_MS, (): number => {
    return performance.now()
  })

  /**
   * Sends a message into the frame over the port.
   * @param message One of the four messages the shell may send.
   * @returns Nothing.
   */
  const post = (message: ModuleShellMessage): void => {
    port?.postMessage(message)
  }

  /**
   * Stops listening, closes the port and cancels the deadline.
   * @returns Nothing.
   */
  const close = (): void => {
    closed = true
    lifetime.abort()
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
    }
    if (port !== null) {
      port.onmessage = null
      port.close()
      port = null
    }
  }

  /**
   * Drops the frame: closes the bridge, marks the mount unavailable and tells the screen why.
   * @param reason Why the frame is no longer shown.
   * @returns Nothing.
   */
  const drop = (reason: ModuleFailureReason): void => {
    if (closed) {
      return
    }
    close()
    state.value = { state: 'unavailable', reason }
    options.onUnavailable({ reason, violations: [...violations.value] })
  }

  /**
   * Counts a violation and drops the frame when the limit is reached.
   * @param violation What the frame did wrong.
   * @returns Nothing.
   */
  const violate = (violation: ModuleViolation): void => {
    if (closed) {
      return
    }
    violations.value = [...violations.value, violation]
    if (violations.value.length >= MODULE_BRIDGE_VIOLATION_LIMIT) {
      drop('violations')
    }
  }

  /**
   * Relays an `api` message and sends the answer back over the port.
   * @param message The validated message.
   * @returns Resolves once the answer was sent or the bridge closed.
   */
  const answerApi = async (message: ModuleApiMessage): Promise<void> => {
    let status = 0
    let body: unknown = null
    try {
      const result = await options.relay({ method: message.method, path: message.path, body: message.body })
      status = result.status
      body = result.body
    } catch {
      // `relay` is specified never to reject; if it does anyway, the frame still gets an answer.
      status = 0
      body = null
    }
    if (!closed) {
      post({ v: MODULE_BRIDGE_VERSION, type: 'api-result', id: message.id, status, body })
    }
  }

  /**
   * Handles one message that arrived over the port.
   * @param event The port's message event.
   * @returns Nothing.
   */
  const onPortMessage = (event: MessageEvent<unknown>): void => {
    if (closed) {
      return
    }
    // Counted before any parsing, so a flood costs the shell one counter increment per message.
    if (messageTypeOf(event.data) === 'api' && !allowApi()) {
      violate('rate')
      return
    }
    const check = checkFrameMessage(event.data, options.routePaths)
    if (!check.ok) {
      violate(check.violation)
      return
    }
    const { message } = check
    switch (message.type) {
      case 'resize':
        options.onResize(clampFrameHeight(message.height))
        break
      case 'navigate':
        options.onNavigate(message.path)
        break
      case 'toast':
        options.onToast(message.text)
        break
      case 'api':
        void answerApi(message)
        break
    }
  }

  /**
   * Handles a message on the shell's window: the only one that is meaningful is the frame's `ready`.
   * @param event The window's message event.
   * @returns Nothing.
   */
  const onWindowMessage = (event: MessageEvent<unknown>): void => {
    const frameWindow = options.frameWindow()
    // Not this frame's: another window's message is none of this bridge's business and is not
    // counted against the frame (a second module's frame, an extension, the page itself).
    if (closed || frameWindow === null || event.source !== frameWindow) {
      return
    }
    const check = checkReadyMessage(event.data)
    if (!check.ok) {
      violate(check.violation)
      return
    }
    if (accepted) {
      violate('second-ready')
      return
    }
    if (check.message.nonce !== options.nonce) {
      violate('bad-nonce')
      return
    }
    accepted = true
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
    }
    const channel = new MessageChannel()
    port = channel.port1
    port.onmessage = onPortMessage
    frameWindow.postMessage({ v: MODULE_BRIDGE_VERSION, type: 'init', ...options.init() }, '*', [channel.port2])
    state.value = { state: 'ready' }
    options.onReady()
  }

  /**
   * Starts listening and arms the handshake deadline.
   * @returns Nothing.
   */
  const open = (): void => {
    window.addEventListener('message', onWindowMessage, { signal: lifetime.signal })
    timer = setTimeout((): void => {
      drop('timeout')
    }, MODULE_BRIDGE_READY_TIMEOUT_MS)
  }

  /**
   * Drops the frame because the iframe reported a load error.
   * @returns Nothing.
   */
  const fail = (): void => {
    drop('load-error')
  }

  /**
   * Tells the frame the interface language changed.
   * @param locale The new language.
   * @returns Nothing.
   */
  const sendLocale = (locale: string): void => {
    post({ v: MODULE_BRIDGE_VERSION, type: 'locale', locale })
  }

  /**
   * Tells the frame the colour scheme changed.
   * @param theme The new scheme.
   * @returns Nothing.
   */
  const sendTheme = (theme: ModuleBridgeTheme): void => {
    post({ v: MODULE_BRIDGE_VERSION, type: 'theme', theme })
  }

  return { state, violationCount, open, fail, sendLocale, sendTheme, close }
}
