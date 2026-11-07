import type { ServerSentEvent } from '../types/api'

/**
 * Decodes one Server-Sent Events frame, the text between two blank lines, into an event.
 * @param frame The raw frame, without its terminating blank line.
 * @returns The decoded event, or `null` for a frame carrying no data (a comment or a keep-alive,
 * which the server sends purely to hold the connection open).
 */
export const decodeServerSentFrame = (frame: string): ServerSentEvent | null => {
  let name = 'message'
  const dataLines: string[] = []

  for (const line of frame.split('\n')) {
    // A line starting with ':' is a comment (the keep-alive form), and carries nothing.
    if (line.startsWith(':')) {
      continue
    }

    const separator = line.indexOf(':')
    const field = separator === -1 ? line : line.slice(0, separator)
    // The SSE spec strips exactly one leading space from the value, and only one.
    const rawValue = separator === -1 ? '' : line.slice(separator + 1)
    const value = rawValue.startsWith(' ') ? rawValue.slice(1) : rawValue

    if (field === 'event') {
      name = value
    } else if (field === 'data') {
      dataLines.push(value)
    }
  }

  if (dataLines.length === 0) {
    return null
  }

  return { name, data: dataLines.join('\n') }
}
