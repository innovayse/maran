/**
 * Copies a record without one key, as a new object so a store's change is observed.
 * @param record The record.
 * @param key The key to leave out.
 * @returns The record without the key.
 */
export const withoutKey = <T>(record: Record<string, T>, key: string): Record<string, T> => {
  return Object.fromEntries(
    Object.entries(record).filter(([name]) => {
      return name !== key
    }),
  )
}
