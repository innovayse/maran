/** Encodes text as UTF-8 to count its bytes, the way a serialised message is measured. */
const ENCODER = new TextEncoder()

/**
 * Counts the UTF-8 bytes of a text.
 *
 * @param text The text to measure.
 * @returns The number of bytes it occupies.
 */
export const byteLength = (text: string): number => {
  return ENCODER.encode(text).length
}
