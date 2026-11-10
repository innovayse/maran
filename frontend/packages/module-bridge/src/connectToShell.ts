import { BRIDGE_VERSION, DEFAULT_INIT_TIMEOUT_MS } from './constants/bridgeLimits'
import { createConnection } from './createConnection'
import type { ConnectOptions, ShellConnection } from './types/moduleBridge'
import { parseInit } from './utils/parseInit'
import { readNonce } from './utils/readNonce'

/**
 * Connects a packaged module's frame to the panel shell: the module's single way out of its sandbox.
 *
 * The frame has an opaque origin, no cookies and no network (the panel's CSP forbids every request), so
 * the handshake is: read the nonce the shell put in the address, announce `ready` to the parent with the
 * nonce (target origin `*`, because an opaque origin cannot be named), and wait for `init`, which
 * carries the port every later message travels over. Only a message from the parent window that carries
 * exactly one port is taken as `init`.
 *
 * @param options Optional settings; see `ConnectOptions`.
 * @returns The connection, once the shell has answered.
 * @throws Error (as a rejection) when the page carries no nonce, or the shell does not answer in time.
 */
export const connectToShell = (options: ConnectOptions = {}): Promise<ShellConnection> => {
  return new Promise<ShellConnection>((resolve, reject): void => {
    let nonce: string

    try {
      nonce = readNonce(window.location.hash)
    } catch (cause) {
      reject(cause instanceof Error ? cause : new Error('The handshake nonce could not be read.'))
      return
    }

    /**
     * Gives the handshake up when the shell has not answered in time. It names `onMessage`, declared
     * below, which is only read when the timer fires, after both exist.
     */
    const giveUp = (): void => {
      window.removeEventListener('message', onMessage)
      reject(new Error('The Maran shell did not answer the handshake in time.'))
    }

    /** The pending give-up timer, cleared once the shell answers. */
    const timer = window.setTimeout(giveUp, options.timeoutMs ?? DEFAULT_INIT_TIMEOUT_MS)

    /**
     * Takes the shell's `init` and finishes the handshake; ignores everything else.
     *
     * @param event A message received by the frame's window.
     */
    const onMessage = (event: MessageEvent<unknown>): void => {
      // Only the parent window can be the shell, and `init` is the only message taken before the port exists.
      if (event.source !== window.parent) {
        return
      }

      const init = parseInit(event.data)
      const port = event.ports[0]

      if (init === null || port === undefined || event.ports.length !== 1) {
        return
      }

      window.clearTimeout(timer)
      window.removeEventListener('message', onMessage)
      resolve(createConnection(init, port))
    }

    // Listening starts before `ready` is posted, so a fast shell cannot answer into a frame not yet listening.
    window.addEventListener('message', onMessage)

    window.parent.postMessage({ v: BRIDGE_VERSION, type: 'ready', nonce }, '*')
  })
}
