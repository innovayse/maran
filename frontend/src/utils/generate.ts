/**
 * Generates a password for a field that is SETTING one, so a person choosing a
 * new credential never has to invent it themselves.
 *
 * The alphabet and the length are not free choices here. They are copied from
 * `ProvisionedPasswordGenerator` in the backend, which in turn matches the
 * `Password` type in the agent (`agent-core/src/validation/secrets/password.rs`):
 * ASCII letters, ASCII digits and exactly five symbols. A generated value that
 * used one character outside that set — a quote, a backslash, a space — would
 * pass every check in the browser and then be refused by the server at the far
 * end of the request, which is the worst place to learn about a disagreement
 * over a character set. The set is written out as a literal rather than
 * assembled from ranges, so a reader can compare it against the other two by
 * eye.
 */

/**
 * Every character a generated password may contain. Identical, character for
 * character, to the backend's `ProvisionedPasswordGenerator.Alphabet`.
 */
const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.=+'

/**
 * How many characters a generated password has. Twenty-four of this alphabet is
 * roughly 145 bits, the same figure the backend generator settled on.
 */
const LENGTH = 24

/**
 * The largest byte value that can be used without bias. 256 is not a multiple
 * of the alphabet's length, so the last, incomplete run of bytes would favour
 * the alphabet's first few characters; bytes at or above this ceiling are drawn
 * again instead of being folded in.
 */
const UNBIASED_CEILING = Math.floor(256 / ALPHABET.length) * ALPHABET.length

/**
 * Produces one random password from {@link ALPHABET}.
 *
 * Randomness comes from `crypto.getRandomValues`, never from `Math.random`,
 * which is not a cryptographic source and is seeded predictably enough that a
 * password from it is a guess away.
 *
 * @returns A newly generated password of {@link LENGTH} characters.
 */
export const generatePassword = (): string => {
  const characters: string[] = []

  while (characters.length < LENGTH) {
    // A whole draw at a time, rather than one byte per character: rejected
    // bytes are then paid for in a smaller number of calls to the entropy
    // source, and the loop still terminates on the count, not on the draw.
    const draw = new Uint8Array(LENGTH)
    crypto.getRandomValues(draw)

    for (const byte of draw) {
      if (byte >= UNBIASED_CEILING) {
        continue
      }

      characters.push(ALPHABET[byte % ALPHABET.length])

      if (characters.length === LENGTH) {
        break
      }
    }
  }

  return characters.join('')
}
