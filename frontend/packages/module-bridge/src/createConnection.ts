import {
  API_RATE_WINDOW_MS,
  BRIDGE_VERSION,
  MAX_API_MESSAGES_PER_WINDOW,
  MAX_API_MESSAGE_BYTES,
} from './constants/bridgeLimits'
import type { BridgeApiMethod, BridgeApiResult, BridgeTheme, ModuleInit, ShellConnection } from './types/moduleBridge'
import { byteLength } from './utils/byteLength'
import { checkApiRequest } from './utils/checkApiRequest'
import { checkNavigatePath } from './utils/checkNavigatePath'
import { checkToastText } from './utils/checkToastText'
import { createRateLimiter } from './utils/createRateLimiter'
import { parseShellMessage } from './utils/parseShellMessage'
import { toResizeHeight } from './utils/toResizeHeight'

/** One request waiting for the shell's answer. */
interface PendingRequest {
  /** Settles the request with the shell's answer. A request the shell never answers stays pending: the shell, not the frame, owns the timeout. */
  resolve: (result: BridgeApiResult) => void
}

/**
 * Builds the connection object over the port the shell transferred. Everything the frame does after the
 * handshake goes through this object, over the port, and nothing else reaches the panel.
 *
 * @param init What the shell said at the handshake.
 * @param port The shell's end of the channel, transferred with `init`.
 * @returns The connection.
 */
export const createConnection = (init: ModuleInit, port: MessagePort): ShellConnection => {
  /** The requests sent and not yet answered, by id. */
  const pending = new Map<string, PendingRequest>()
  /** Who wants to hear about a language change. */
  const localeHandlers = new Set<(locale: string) => void>()
  /** Who wants to hear about a colour scheme change. */
  const themeHandlers = new Set<(theme: BridgeTheme) => void>()
  /** Enforces the shell's rate limit before a message is sent. */
  const allowApiMessage = createRateLimiter(MAX_API_MESSAGES_PER_WINDOW, API_RATE_WINDOW_MS, (): number => {
    return performance.now()
  })
  /** The counter behind request ids; an id is `r` and a number, inside the shell's 64-character alphabet. */
  let lastRequestId = 0

  /**
   * Routes one message from the shell. Anything that is not a well-formed `api-result`, `locale` or
   * `theme` is ignored.
   *
   * @param event The port message.
   */
  port.onmessage = (event: MessageEvent<unknown>): void => {
    const message = parseShellMessage(event.data)

    if (message === null) {
      return
    }

    if (message.type === 'api-result') {
      const waiting = pending.get(message.id)
      pending.delete(message.id)
      waiting?.resolve({ status: message.status, body: message.body })
    } else if (message.type === 'locale') {
      localeHandlers.forEach((handler): void => {
        handler(message.locale)
      })
    } else {
      themeHandlers.forEach((handler): void => {
        handler(message.theme)
      })
    }
  }

  /**
   * Asks the shell to perform an API call with its own session.
   *
   * @param method The HTTP method.
   * @param path The API path, under `/api/v1/`.
   * @param body The JSON body, if any.
   * @returns The status and parsed body the API answered with; rejects, without sending, on a refused request.
   */
  const request = (method: BridgeApiMethod, path: string, body?: unknown): Promise<BridgeApiResult> => {
    const checked = checkApiRequest(method, path, body)

    if (!checked.ok) {
      return Promise.reject(new Error(checked.reason))
    }

    lastRequestId += 1
    const id = `r${lastRequestId}`
    const message: Record<string, unknown> = { v: BRIDGE_VERSION, type: 'api', id, method, path }

    if (body !== undefined) {
      message.body = body
    }

    if (byteLength(JSON.stringify(message)) > MAX_API_MESSAGE_BYTES) {
      return Promise.reject(new Error('The request is larger than the shell accepts.'))
    }

    if (!allowApiMessage()) {
      return Promise.reject(new Error('Too many requests: the shell accepts 30 per second.'))
    }

    return new Promise<BridgeApiResult>((resolve): void => {
      pending.set(id, { resolve })
      port.postMessage(message)
    })
  }

  /**
   * Asks the shell to open another declared route of this module.
   *
   * @param path The module-relative path.
   */
  const navigate = (path: string): void => {
    const refusal = checkNavigatePath(path)

    if (refusal !== null) {
      throw new Error(refusal)
    }

    port.postMessage({ v: BRIDGE_VERSION, type: 'navigate', path })
  }

  /**
   * Reports the content height so the shell can size the frame.
   *
   * @param height The height in CSS pixels.
   */
  const resize = (height: number): void => {
    port.postMessage({ v: BRIDGE_VERSION, type: 'resize', height: toResizeHeight(height) })
  }

  /**
   * Asks the shell to show a plain-text notification.
   *
   * @param text The text, 1 to 200 characters.
   */
  const toast = (text: string): void => {
    const refusal = checkToastText(text)

    if (refusal !== null) {
      throw new Error(refusal)
    }

    port.postMessage({ v: BRIDGE_VERSION, type: 'toast', text })
  }

  /**
   * Subscribes to interface language changes.
   *
   * @param handler Called with the new locale.
   * @returns The function that unsubscribes.
   */
  const onLocale = (handler: (locale: string) => void): (() => void) => {
    localeHandlers.add(handler)

    /** Stops delivering locale changes to this handler. */
    return (): void => {
      localeHandlers.delete(handler)
    }
  }

  /**
   * Subscribes to colour scheme changes.
   *
   * @param handler Called with the new scheme.
   * @returns The function that unsubscribes.
   */
  const onTheme = (handler: (theme: BridgeTheme) => void): (() => void) => {
    themeHandlers.add(handler)

    /** Stops delivering theme changes to this handler. */
    return (): void => {
      themeHandlers.delete(handler)
    }
  }

  return { init, request, navigate, resize, toast, onLocale, onTheme }
}
