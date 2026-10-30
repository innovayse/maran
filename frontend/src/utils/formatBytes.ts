/**
 * Turning a byte count into something a person reads, for the monitoring screens.
 *
 * Binary steps (1024), not decimal ones, and the unit names say so: the figures come from the
 * kernel by way of the agent — `/proc/meminfo` and `statvfs` — which count in binary multiples, and
 * a panel that divided them by 1000 would report a 16 GiB machine as having 17.2 of something.
 *
 * The unit suffix is produced here rather than translated, deliberately. `KiB`/`MiB`/`GiB` are
 * IEC symbols, not English words: they are the same characters in every locale this panel ships,
 * so putting them in the message bundles would create three copies of one constant and three
 * chances for one of them to be typed differently.
 */

/** The binary step between two adjacent units. */
const STEP = 1024

/** The unit symbols, smallest first — the index into this list IS the power of {@link STEP}. */
const UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB'] as const

/**
 * The symbol beside a figure {@link bytesToGibibytes} produced, exported so a chart's unit label
 * and this file's own suffixes are one constant. The monitoring screen once spelled it twice — the
 * disk table read `GiB` from here while the memory chart read `ГиБ` from a message bundle — and
 * two spellings of one unit on one screen is the drift the doc comment above predicts.
 */
export const GIBIBYTE_UNIT: string = UNITS[3]

/**
 * Converts a byte count to a whole number of gibibytes and fractions of one.
 *
 * Used where a whole SERIES has to share one unit — a chart's y-axis cannot relabel itself per
 * point — so the unit is fixed rather than chosen per value the way {@link formatBytes} chooses it.
 * @param bytes The byte count to convert.
 * @returns The same quantity expressed in GiB.
 */
export const bytesToGibibytes = (bytes: number): number => {
  return bytes / (STEP * STEP * STEP)
}

/**
 * Formats a byte count with the largest unit that leaves a number at least 1.
 *
 * A negative count cannot arise from any figure this panel receives — every one of them is a
 * measured size — so it is formatted as plain bytes rather than guarded against with a branch that
 * no input reaches.
 * @param bytes The byte count to format.
 * @returns The count with its IEC unit, e.g. `1.5 GiB`; whole numbers keep no decimal point.
 */
export const formatBytes = (bytes: number): string => {
  let value = bytes
  let unitIndex = 0

  // Stops at the last unit rather than running off the end of the list: a figure large enough to
  // need one beyond PiB does not exist on a machine this panel runs on.
  while (Math.abs(value) >= STEP && unitIndex < UNITS.length - 1) {
    value /= STEP
    unitIndex += 1
  }

  const rounded = Math.round(value * 10) / 10
  const text = Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1)
  return `${text} ${UNITS[unitIndex]}`
}

/**
 * Formats a size a plan or an account states in megabytes — the unit the panel's quotas are
 * written in — with the largest binary unit that leaves a number at least 1, so a quota of
 * `1048576` reads `1 TiB` rather than a figure nobody can size at a glance.
 *
 * The quota's megabyte is the binary one: the agent writes it to the kernel as 1024 × 1024 bytes,
 * which is why it can be fed straight into {@link formatBytes}.
 * @param megabytes The quota as the panel reports it.
 * @returns The quota with its IEC unit, e.g. `2 MiB`, `1 TiB`.
 */
export const formatMegabytes = (megabytes: number): string => {
  return formatBytes(megabytes * STEP * STEP)
}
