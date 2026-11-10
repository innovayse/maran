import { MODULE_BRIDGE_NONCE_BYTES } from '../types/moduleBridge'

/**
 * Mints the nonce of one frame mount: the secret the frame must echo in `ready`, so a message from
 * any other window, or from an earlier mount, is not taken for this frame's handshake.
 * @returns Twice as many lowercase hex characters as {@link MODULE_BRIDGE_NONCE_BYTES}, from the
 * browser's cryptographic random source.
 */
export const mintBridgeNonce = (): string => {
  const bytes = new Uint8Array(MODULE_BRIDGE_NONCE_BYTES)
  crypto.getRandomValues(bytes)
  return Array.from(bytes, (byte): string => {
    return byte.toString(16).padStart(2, '0')
  }).join('')
}
