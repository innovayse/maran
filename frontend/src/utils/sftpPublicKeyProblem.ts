/** Why a pasted SSH public key cannot be sent; each value names a message under `sftp.keys.form.errors`. */
export type SftpPublicKeyProblem = 'empty' | 'multiline' | 'tooLong' | 'algorithm' | 'format'

/**
 * The key types the agent accepts, copied from its allow-list (`ssh_public_key.rs`). This is advice
 * that saves a round trip: the server re-validates, and its own message wins on any disagreement.
 */
const ACCEPTED_ALGORITHMS: readonly string[] = [
  'ssh-ed25519',
  'sk-ssh-ed25519@openssh.com',
  'ecdsa-sha2-nistp256',
  'ecdsa-sha2-nistp384',
  'ecdsa-sha2-nistp521',
  'sk-ecdsa-sha2-nistp256@openssh.com',
  'ssh-rsa',
]

/** The longest key text the agent accepts, in bytes; a 16384-bit RSA key fits under it. */
const MAX_KEY_BYTES = 8 * 1024

/** The base64 alphabet with optional padding; whether it decodes to a real key is the server's call. */
const BASE64_PATTERN = /^[A-Za-z0-9+/]+={0,2}$/

/**
 * Whether the text holds a control character (a tab, an escape, DEL), which no key line contains.
 * @param text The text to scan.
 * @returns True when one is present.
 */
const hasControlCharacter = (text: string): boolean => {
  return Array.from(text).some((character) => {
    const code = character.codePointAt(0) ?? 0
    return code < 0x20 || code === 0x7f
  })
}

/**
 * Checks pasted key text against the shape the server accepts: a single line of
 * `<algorithm> <base64> [comment]`. It errs permissive — it never decodes the key or measures an RSA
 * modulus — so a refusal here is certain and an acceptance is not a promise.
 * @param text The text the person pasted.
 * @returns The first problem found, or `null` when the text may be sent.
 */
export const sftpPublicKeyProblem = (text: string): SftpPublicKeyProblem | null => {
  const key = text.trim()
  if (key === '') {
    return 'empty'
  }
  if (/[\r\n]/.test(key)) {
    return 'multiline'
  }
  if (new TextEncoder().encode(key).length > MAX_KEY_BYTES) {
    return 'tooLong'
  }
  if (hasControlCharacter(key)) {
    return 'format'
  }
  const [algorithm, data] = key.split(' ').filter((field) => {
    return field !== ''
  })
  if (algorithm === undefined || !ACCEPTED_ALGORITHMS.includes(algorithm)) {
    return 'algorithm'
  }
  return data === undefined || !BASE64_PATTERN.test(data) ? 'format' : null
}
