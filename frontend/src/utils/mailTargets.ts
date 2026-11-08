/**
 * Reads the destinations a person typed into an alias form: one address per line, or several
 * separated by commas, with the blanks left out. Only the shape of the list is read here — whether
 * an entry is a legal address, an outside one, or a loop is the server's to decide.
 * @param text What the textarea holds.
 * @returns The entries in the order typed, each trimmed, none empty.
 */
export const parseMailTargets = (text: string): string[] => {
  return text
    .split(/[\n,]+/)
    .map((entry) => {
      return entry.trim()
    })
    .filter((entry) => {
      return entry !== ''
    })
}
