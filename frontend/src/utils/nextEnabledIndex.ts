/** An entry in a keyboard-navigable list, which may be present but not choosable. */
interface EnableableEntry {
  /** Whether the entry is skipped by keyboard navigation. */
  disabled?: boolean
}

/**
 * Finds the next choosable entry when moving through a list with the arrow keys.
 *
 * Disabled entries are stepped over rather than landed on: the ARIA listbox and
 * menu patterns both require that an unavailable option never takes focus, or a
 * keyboard user gets stuck on a row they cannot act on. The search stops at the
 * ends instead of wrapping, so holding an arrow key settles at the boundary
 * rather than cycling forever.
 * @param entries The list being navigated.
 * @param from Index to search away from; -1 starts before the first entry.
 * @param step Direction: 1 forwards, -1 backwards.
 * @returns The index of the next choosable entry, or `from` when there is none.
 */
export const nextEnabledIndex = <T extends EnableableEntry>(
  entries: readonly T[],
  from: number,
  step: number,
): number => {
  for (let index = from + step; index >= 0 && index < entries.length; index += step) {
    if (entries[index]?.disabled !== true) {
      return index
    }
  }

  return from
}
