/**
 * The limits of bridge protocol v1. They are the shell's limits, repeated so a frame refuses locally
 * what the shell would drop and count: three such violations unmount the frame.
 */

/** The protocol version carried in every message. */
export const BRIDGE_VERSION = 1

/** How long to wait for `init` by default, in milliseconds (the shell waits 10 seconds for `ready`). */
export const DEFAULT_INIT_TIMEOUT_MS = 10_000

/** The length of the handshake nonce: 32 lowercase hexadecimal characters. */
export const NONCE_LENGTH = 32

/** The largest serialised message other than `api`, in bytes. */
export const MAX_MESSAGE_BYTES = 64 * 1024

/** The largest serialised JSON body of an `api` message, in bytes. */
export const MAX_API_BODY_BYTES = 1024 * 1024

/** The largest serialised `api` message, in bytes: the body limit plus an allowance for the envelope. */
export const MAX_API_MESSAGE_BYTES = MAX_API_BODY_BYTES + 4 * 1024

/** The most `api` messages a frame may send in one rate window. */
export const MAX_API_MESSAGES_PER_WINDOW = 30

/** The length of the rate window, in milliseconds. */
export const API_RATE_WINDOW_MS = 1000

/** The longest request id the shell accepts. */
export const MAX_REQUEST_ID_LENGTH = 64

/** The longest toast text, in characters. */
export const MAX_TOAST_LENGTH = 200

/** The longest module-relative path, in characters. */
export const MAX_PATH_LENGTH = 512

/** The prefix every `api` path must start with. The server decides what the module may actually reach. */
export const API_PATH_PREFIX = '/api/v1/'

/** The methods the shell performs. */
export const API_METHODS: readonly string[] = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']
