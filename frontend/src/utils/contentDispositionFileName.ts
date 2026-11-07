/** Matches the RFC 5987 extended form, `filename*=UTF-8''maran%20audit.csv`. */
const EXTENDED_FILE_NAME = /filename\*\s*=\s*([^']*)'[^']*'([^;]+)/i

/** Matches the plain form, quoted (`filename="a.csv"`) or bare (`filename=a.csv`). */
const PLAIN_FILE_NAME = /filename\s*=\s*(?:"([^"]*)"|([^;]+))/i

/**
 * Reads the file name a download response proposes, from its `Content-Disposition` header.
 *
 * The extended form wins when both are present, as RFC 6266 asks, because it is the one that can
 * carry a name outside ASCII. A name that carries a path separator is reduced to its last segment:
 * the header is the server's suggestion for a file the browser writes into the reader's own
 * download folder, and a suggestion must never be able to name a folder.
 * @param header The raw `Content-Disposition` value, or `null` when the response had none.
 * @returns The proposed file name, or `null` when the header names none.
 */
export const contentDispositionFileName = (header: string | null): string | null => {
  if (header === null) {
    return null
  }

  const extended = EXTENDED_FILE_NAME.exec(header)
  const plain = PLAIN_FILE_NAME.exec(header)
  let raw: string | undefined = plain?.[1] ?? plain?.[2]?.trim()

  if (extended?.[2] !== undefined) {
    try {
      raw = decodeURIComponent(extended[2].trim())
    } catch {
      // A malformed percent sequence: the plain form, if any, is the best remaining answer.
    }
  }

  if (raw === undefined) {
    return null
  }

  const name = raw.split(/[\\/]/).pop() ?? ''
  return name === '' ? null : name
}
