import { isValid, parse } from 'date-fns'

/**
 * Whether a text is a real calendar date written `YYYY-MM-DD`, the form a date filter keeps in the
 * URL. A hand-edited link holding anything else is read as no filter rather than sent on.
 * @param text The text to check.
 * @returns True for a well-formed date that exists (`2026-02-30` is refused).
 */
export const isCalendarDate = (text: string): boolean => {
  return /^\d{4}-\d{2}-\d{2}$/.test(text) && isValid(parse(text, 'yyyy-MM-dd', new Date()))
}
