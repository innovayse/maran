/**
 * Choosing the unit a chart of byte rates is drawn in.
 *
 * A chart's axis has one unit for the whole series, so the unit is chosen from the series' largest
 * value: the biggest binary step that still leaves that value at least 1. A quiet server's network
 * moves a few KiB a second, and drawn in MiB/s every tick read `0.00`; drawn in KiB/s the same
 * series reads `3.4`, `7.1`, `12` (#222). The symbols are IEC notation, locale-invariant, for the
 * reason `formatBytes.ts` gives.
 */

import { BINARY_STEP } from './formatBytes'

/** The rate units, smallest first — the index is the power of {@link BINARY_STEP}. */
const RATE_UNITS = ['B/s', 'KiB/s', 'MiB/s', 'GiB/s'] as const

/** The unit a series is drawn in, and what to divide each byte rate by to express it in that unit. */
export interface ByteRateScale {
  /** The unit symbol for the axis and the readings. */
  unit: string
  /** Bytes per second in one of {@link unit}. */
  divisor: number
}

/**
 * Picks the unit for a series of byte rates.
 * @param rates The series' rates, in bytes per second.
 * @returns The largest unit that leaves the series' peak at least 1; bytes per second for an empty
 * or all-zero series.
 */
export const byteRateScale = (rates: readonly number[]): ByteRateScale => {
  const peak = rates.reduce((largest, rate) => {
    return Math.max(largest, Math.abs(rate))
  }, 0)

  let index = 0
  while (index < RATE_UNITS.length - 1 && peak >= BINARY_STEP ** (index + 1)) {
    index += 1
  }

  return { unit: RATE_UNITS[index], divisor: BINARY_STEP ** index }
}

/**
 * Formats a reading with as many decimals as its size needs: two below 10, one below 100, none above.
 * Small readings keep their digits, large ones do not carry noise.
 * @param value The reading, already in the chart's unit.
 * @returns The reading as text.
 */
export const formatRateReading = (value: number): string => {
  const magnitude = Math.abs(value)
  if (magnitude < 10) {
    return value.toFixed(2)
  }
  return magnitude < 100 ? value.toFixed(1) : value.toFixed(0)
}
