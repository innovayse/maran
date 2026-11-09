/**
 * Pure lookups between a live chart's readings, its time axis and a place along its plot, for
 * {@link ../components/ui/UiLiveChart.vue | UiLiveChart}: the value nearest an instant, the instant
 * nearest a place, and the place of an instant. Nothing here touches Vue, i18n or the DOM.
 */
import { LIVE_CHART_WIDTH, nearestMeasuredIndex, timeToX, type LiveChartDomain, type LiveChartPoint } from './liveChartGeometry'

/**
 * Finds the reading nearest an instant.
 * @param points The series, oldest first.
 * @param at The instant, epoch milliseconds.
 * @returns The value, or `null` when the series holds no reading.
 */
export const valueNear = (points: LiveChartPoint[], at: number): number | null => {
  const index = nearestMeasuredIndex(points, at)
  return index === null ? null : points[index].value
}

/**
 * Finds the instant of the reading nearest a place along the plot.
 * @param points The series the pointer is snapped to, oldest first.
 * @param domain The time span the axis covers.
 * @param fraction The place as a fraction of the plot width in 0-1.
 * @returns The instant (epoch milliseconds), or `null` when the series holds no reading.
 */
export const instantAtFraction = (points: LiveChartPoint[], domain: LiveChartDomain, fraction: number): number | null => {
  const index = nearestMeasuredIndex(points, domain.from + fraction * (domain.to - domain.from))
  return index === null ? null : points[index].at
}

/**
 * Converts an instant to a place along the plot.
 * @param at The instant, or `null`.
 * @param domain The time span the axis covers, or `null` when there is no data.
 * @returns A fraction of the plot width in 0-1, or `null` when there is no instant or it lies outside the axis.
 */
export const fractionOfInstant = (at: number | null, domain: LiveChartDomain | null): number | null => {
  if (at === null || domain === null || at < domain.from || at > domain.to) {
    return null
  }
  return timeToX(at, domain) / LIVE_CHART_WIDTH
}
