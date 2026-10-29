/**
 * The one way this SPA derives the one or two letters of an avatar from a name.
 *
 * Three places draw an initials disc (the shell's identity badge, the accounts table, the
 * dashboard's audit journal) and the names they hold are all machine names: a login, an account's
 * short name, a domain label. Those are chosen by their owners and split on punctuation, not on
 * spaces, and nothing about them is guaranteed to be Latin. Two callers deriving this separately is
 * how two discs come to show different letters for the same person, so the rule lives here once.
 *
 * The rule:
 * - The name is cut into words at every run of characters that are neither a letter nor a digit
 *   (`gyumri-kitchen`, `john.doe`, `anna_k`, `Ann Lee`). Two or more words give the first character
 *   of the first two words (`GK`); a single word gives its first two characters (`aramyan` reads
 *   `AR`).
 * - Letters are Unicode letters, so an Armenian, Cyrillic or Greek name is cut where a person would
 *   cut it instead of being treated as one unbroken punctuation-free blob.
 * - A name with no letter or digit at all (`---`, an emoji) falls back to its own first character
 *   rather than drawing an empty disc.
 * - Characters are counted as code points, never UTF-16 units, so an astral character is not cut in
 *   half; and the text is upper-cased BEFORE the cut, because some letters grow when upper-cased
 *   (Armenian `և` becomes `ԵՒ`, German `ß` becomes `SS`) and the result must never exceed two.
 * - An empty name gives an empty string; the caller decides what to draw instead.
 *
 * @param name The text the initials come from: a login, an account name, any machine name.
 * @returns At most two upper-cased characters, or `''` for an empty name.
 */
export const initialsOf = (name: string): string => {
  const words = name.split(/[^\p{L}\p{N}]+/u).filter((word) => {
    return word.length > 0
  })
  let letters: string
  if (words.length >= 2) {
    letters = `${Array.from(words[0])[0]}${Array.from(words[1])[0]}`
  } else if (words.length === 1) {
    letters = words[0]
  } else {
    // Nothing but punctuation or symbols: show the first thing the name actually starts with.
    letters = Array.from(name)[0] ?? ''
  }
  return Array.from(letters.toUpperCase()).slice(0, 2).join('')
}
