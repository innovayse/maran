/**
 * The character-class rules the cron mirrors share: what a control character is, and what the panel
 * counts as whitespace.
 *
 * Both live here rather than in the two mirrors that use them (`cronSchedule.ts` and
 * `cronCommand.ts`) because they are the same question asked twice, and because getting the second
 * one subtly wrong is easy in exactly one direction. They are written as explicit code-point tests
 * rather than as regular expressions: a pattern carrying literal control characters is unreadable
 * in a diff, and the class it describes is precisely what a reader has to check against the module.
 */

/**
 * The whitespace characters .NET's `char.IsWhiteSpace` reports and that are NOT control characters.
 *
 * Spelled out rather than left to JavaScript's `\s`, and the difference matters in the direction a
 * mirror must not be wrong in: `\s` also matches the byte-order mark, which .NET does not call
 * whitespace. Using it would make this panel refuse input the module accepts — a client narrowing
 * what the server allows.
 *
 * The control characters that are also whitespace — tab, newline, carriage return, form feed,
 * vertical tab, and NEL — are absent on purpose: {@link hasControlCharacter} already covers them,
 * and listing them twice would invite the two lists to drift.
 */
const WHITESPACE = new Set([
  '\u0020',
  '\u00a0',
  '\u1680',
  '\u2000',
  '\u2001',
  '\u2002',
  '\u2003',
  '\u2004',
  '\u2005',
  '\u2006',
  '\u2007',
  '\u2008',
  '\u2009',
  '\u200a',
  '\u2028',
  '\u2029',
  '\u202f',
  '\u205f',
  '\u3000',
])

/** The last code point of the C0 control range. */
const C0_END = 0x1f

/** The first code point of the DEL-and-C1 control range. */
const C1_START = 0x7f

/** The last code point of the C1 control range. */
const C1_END = 0x9f

/**
 * Whether a single character is one .NET's `char.IsControl` reports.
 * @param character The character to classify.
 * @returns True for the C0 range, DEL, and the C1 range.
 */
const isControlCharacter = (character: string): boolean => {
  const code = character.codePointAt(0) ?? 0
  return code <= C0_END || (code >= C1_START && code <= C1_END)
}

/**
 * Whether the text carries a control character anywhere.
 *
 * This is what a value bound for a single line of a file may not contain — a newline most of all,
 * since the file the agent writes holds exactly one line.
 * @param text The text to check.
 * @returns True when any character is a control character.
 */
export const hasControlCharacter = (text: string): boolean => {
  return [...text].some(isControlCharacter)
}

/**
 * Whether the text carries whitespace or a control character anywhere.
 *
 * A schedule field may contain neither: a space would smuggle a sixth field past a check meant for
 * five, and the module refuses both classes outright.
 * @param text The text to check.
 * @returns True when any character is whitespace or a control character.
 */
export const hasWhitespaceOrControl = (text: string): boolean => {
  return [...text].some((character) => {
    return isControlCharacter(character) || WHITESPACE.has(character)
  })
}

/**
 * Whether the text begins or ends with whitespace.
 *
 * Refused rather than trimmed wherever it applies: a command is stored verbatim and compared
 * verbatim when the agent decides whether an entry duplicates one already installed, so a leading
 * space and a trailing space must not become two spellings of one command.
 * @param text The text to check; the empty string has no edges and answers false.
 * @returns True when the first or the last character is whitespace.
 */
export const hasEdgeWhitespace = (text: string): boolean => {
  const characters = [...text]
  const first = characters[0]
  const last = characters[characters.length - 1]
  if (first === undefined || last === undefined) {
    return false
  }
  return WHITESPACE.has(first) || WHITESPACE.has(last)
}
