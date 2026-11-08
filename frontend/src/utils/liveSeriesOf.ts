import type { LiveMetricsPoint } from '../types/liveDashboard'
import type { LiveChartPoint } from './liveChartGeometry'

/**
 * Picks one quantity out of a run of host readings as a chart series.
 * @param points The readings, oldest first.
 * @param pick Reads the quantity from one reading; `null` where the reading carries none.
 * @returns One chart point per reading, positioned at the reading's instant.
 */
export const liveSeriesOf = (
  points: readonly LiveMetricsPoint[],
  pick: (point: LiveMetricsPoint) => number | null,
): LiveChartPoint[] => {
  return points.map((point) => {
    return { at: Date.parse(point.t), value: pick(point) }
  })
}
