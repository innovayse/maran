/**
 * What an optional whole-number field holds once read: nothing typed, a figure, or text that is
 * not a whole number (so the form can refuse it instead of silently sending something else).
 */
export type OptionalWholeNumber =
  /** The field is empty: the caller follows the default. */
  | { kind: 'empty' }
  /** The field holds a non-negative whole number. */
  | {
      /** Discriminator: a figure was typed. */
      kind: 'figure'
      /** The figure. */
      value: number
    }
  /** The field holds something that is not a non-negative whole number. */
  | { kind: 'invalid' }

/** Digits only, no sign, no decimal point, no exponent: the one shape a limit may be typed in. */
const WHOLE_NUMBER = /^\d{1,10}$/

/**
 * Reads the text of an optional whole-number field.
 * @param text The field's text; surrounding spaces are ignored.
 * @returns Empty for blank text, the figure for digits, invalid for anything else.
 */
export const readOptionalWholeNumber = (text: string): OptionalWholeNumber => {
  const trimmed = text.trim()
  if (trimmed === '') {
    return { kind: 'empty' }
  }
  if (!WHOLE_NUMBER.test(trimmed)) {
    return { kind: 'invalid' }
  }
  return { kind: 'figure', value: Number(trimmed) }
}
