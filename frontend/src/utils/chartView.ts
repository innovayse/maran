import { CHART_BASELINE_Y, CHART_MARGIN, CHART_WIDTH } from './chartGeometry'

/** One value label: its pixel row in the plot's coordinate space and its ready-to-render text. */
export interface UiChartYTick {
  /** Vertical position in the SVG's user units; also the label's key. */
  y: number
  /** The formatted value, without its unit. */
  label: string
}

/**
 * The part of the geometry module's coordinate space the SVG actually shows: the plot itself, a
 * few units of breathing room on the left for the first marker, and nothing of the left margin or
 * the bottom strip, which the HTML axis labels occupy instead.
 */
export const CHART_VIEW = {
  x: CHART_MARGIN.left - 4,
  y: 0,
  width: CHART_WIDTH - (CHART_MARGIN.left - 4),
  height: CHART_BASELINE_Y + 4,
}

/**
 * A user-space x position as a percentage of the visible plot's width.
 * @param x A user-space x, as in `ChartPlottedPoint`.
 * @returns The CSS percentage to position an HTML label with.
 */
export const chartXPercent = (x: number): string => {
  return `${((x - CHART_VIEW.x) / CHART_VIEW.width) * 100}%`
}

/**
 * A user-space y position as a percentage of the visible plot's height.
 * @param y A user-space y, as in `ChartPlottedPoint`.
 * @returns The CSS percentage to position an HTML label with.
 */
export const chartYPercent = (y: number): string => {
  return `${(y / CHART_VIEW.height) * 100}%`
}
