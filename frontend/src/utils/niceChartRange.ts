import type { ChartValueRange } from './chartGeometry'

/**
 * Widens a range to round step boundaries so the axis reads in round numbers.
 *
 * The vertical axis used to be labelled with the series' own minimum, midpoint and maximum, so a
 * gridline read `72.00 / 41.00 / 10.00` — true, but a scale nobody can read a third value off. The
 * step is the smallest of 1, 2, 2.5, 5 or 10 times a power of ten that is at least half the
 * span, since the axis has two intervals (max, midpoint, min). The minimum is floored and the
 * maximum ceiled to that step, so a non-negative series stays non-negative (the "never below zero"
 * lesson of `computeValueRange` still holds) and a range whose midpoint is the middle gridline stays
 * a multiple of the step. Results are rounded to twelve significant digits to shed floating-point
 * residue such as `0.30000000000000004`.
 * @param range The data's own range from `computeValueRange`, always of non-zero span.
 * @returns A range of the same shape that contains it and lands on round values.
 */
export const niceChartRange = (range: ChartValueRange): ChartValueRange => {
  const rawStep = (range.max - range.min) / 2
  const magnitude = 10 ** Math.floor(Math.log10(rawStep))
  const step =
    [1, 2, 2.5, 5, 10]
      .map((multiple) => {
        return multiple * magnitude
      })
      .find((candidate) => {
        return candidate >= rawStep
      }) ?? 10 * magnitude
  /**
   * Sheds floating-point residue from a boundary.
   * @param value A step multiple that may carry binary-fraction noise.
   * @returns The value rounded to twelve significant digits.
   */
  const tidy = (value: number): number => {
    return Number(value.toPrecision(12))
  }
  return {
    min: tidy(Math.floor(range.min / step) * step),
    max: tidy(Math.ceil(range.max / step) * step),
  }
}
