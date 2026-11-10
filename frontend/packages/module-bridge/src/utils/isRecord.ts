/**
 * Narrows an unknown value to a plain object.
 *
 * @param value Anything received over the wire.
 * @returns True when the value is a non-null, non-array object.
 */
export const isRecord = (value: unknown): value is Record<string, unknown> => {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
