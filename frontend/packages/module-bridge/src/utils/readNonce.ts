import { NONCE_LENGTH } from '../constants/bridgeLimits'

/** A handshake nonce: exactly `NONCE_LENGTH` lowercase hexadecimal characters. */
const NONCE_PATTERN = new RegExp(`^[0-9a-f]{${NONCE_LENGTH}}$`)

/**
 * Reads the handshake nonce the shell put in the frame address (`...#<nonce>`).
 *
 * @param hash The frame's `location.hash`, with or without its leading `#`.
 * @returns The nonce.
 * @throws Error when the address carries no well-formed nonce, which means the page was not opened by the shell.
 */
export const readNonce = (hash: string): string => {
  const nonce = hash.startsWith('#') ? hash.slice(1) : hash

  if (!NONCE_PATTERN.test(nonce)) {
    throw new Error('The address carries no handshake nonce: this page must be opened by the Maran shell.')
  }

  return nonce
}
