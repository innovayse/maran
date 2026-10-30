/**
 * Reads a list of keys remembered in `localStorage`. Storage can throw (private window, blocked
 * site data), and a remembered preference must never be able to break the page, so any failure,
 * and anything that is not a list of strings, reads as an empty list.
 * @param storageKey The `localStorage` key the list is stored under.
 * @returns The remembered keys, or an empty list.
 */
export const readStoredKeys = (storageKey: string): string[] => {
  try {
    const raw = window.localStorage.getItem(storageKey)
    const parsed: unknown = raw === null ? [] : JSON.parse(raw)
    return Array.isArray(parsed)
      ? parsed.filter((key): key is string => {
          return typeof key === 'string'
        })
      : []
  } catch {
    return []
  }
}
