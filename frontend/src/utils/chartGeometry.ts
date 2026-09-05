/**
 * Pure geometry for {@link ../components/ui/UiChart.vue | UiChart}: scale computation, SVG path
 * building and axis-tick selection, all as plain functions over numbers.
 *
 * Nothing here touches Vue, i18n or the DOM — that is deliberate. `UiChart` owns rendering and
 * interaction (props, refs, pointer events, translated labels); this module owns turning a series
 * of `{ at, value }` buckets into pixel positions and SVG path strings. The split means the part
 * most worth unit-testing in isolation — "does a flat series still produce a non-zero range", "does
 * a single point avoid a degenerate path" — can be exercised without mounting a component.
 *
 * The chart's fixed dimensions live here too, as literal constants, because every pure function
 * below is defined against them: a scale computed against one set of margins and rendered inside
 * another would silently draw wrong, so the numbers and the geometry that depends on them are kept
 * in one file rather than split across the component and this module.
 */

/** One measured value at one instant — the same shape {@link UiChartPoint} sends in. */
export interface ChartPoint {
  /** When the value was recorded, as a Unix epoch in milliseconds. */
  at: number
  /** The measured value, in the series' own unit. */
  value: number
}

/** A {@link ChartPoint}, plus its computed pixel position in the SVG's user-coordinate space. */
export interface ChartPlottedPoint extends ChartPoint {
  /** Horizontal position in the SVG's user units. */
  x: number
  /** Vertical position in the SVG's user units. */
  y: number
}

/** The vertical span a chart's values are scaled against. */
export interface ChartValueRange {
  /** The lowest value the scale covers. */
  min: number
  /** The highest value the scale covers. */
  max: number
}

/** One gridline on the vertical axis: its pixel row and the raw value it represents. */
export interface ChartYTick {
  /** Vertical position in the SVG's user units. */
  y: number
  /** The raw value this gridline sits at — left unformatted, since formatting is caller-specific. */
  value: number
}

/** Which edge of an SVG `<text>` its `x` coordinate anchors to. */
export type ChartTickAnchor = 'start' | 'middle' | 'end'

/** One label on the horizontal axis: its pixel column, the bucket it points at, and its anchor. */
export interface ChartXTick {
  /** Horizontal position in the SVG's user units. */
  x: number
  /** The bucket's instant, left unformatted — formatting a date is a caller concern, not geometry. */
  at: number
  /** Which edge of the label text `x` anchors to, so the first/last labels stay inside the plot. */
  anchor: ChartTickAnchor
}

/** Internal SVG coordinate system, in user units — scaled to the rendered box by the viewBox. */
export const CHART_WIDTH = 560
/** Internal SVG coordinate system, in user units — scaled to the rendered box by the viewBox. */
export const CHART_HEIGHT = 200
/** Space reserved around the plot for axis tick labels. */
export const CHART_MARGIN = { top: 16, right: 12, bottom: 26, left: 46 }
/** The plot area's width, once the left/right margins are removed from {@link CHART_WIDTH}. */
const PLOT_WIDTH = CHART_WIDTH - CHART_MARGIN.left - CHART_MARGIN.right
/** The plot area's height, once the top/bottom margins are removed from {@link CHART_HEIGHT}. */
const PLOT_HEIGHT = CHART_HEIGHT - CHART_MARGIN.top - CHART_MARGIN.bottom
/** The area path's bottom edge — the plot's floor. */
export const CHART_BASELINE_Y = CHART_MARGIN.top + PLOT_HEIGHT
/** Width of the hover readout box, in the same user units. */
export const CHART_READOUT_WIDTH = 108
/** Height of the hover readout box, in the same user units. */
export const CHART_READOUT_HEIGHT = 38

/**
 * Sorts a series by time, oldest first.
 *
 * Defensive rather than trusting the caller's order: a bucket source that emitted out of sequence
 * would otherwise draw a path that zig-zags backwards, which reads as a rendering bug rather than
 * as the data it is.
 * @param series The series as received, in any order.
 * @returns A new array, sorted by {@link ChartPoint.at} ascending.
 */
export const sortChartSeries = (series: ChartPoint[]): ChartPoint[] => {
  return [...series].sort((a, b) => {
    return a.at - b.at
  })
}

/**
 * Computes the vertical range a series scales against. Never zero-width: a flat series (including
 * the single-value case, where min and max are the same number by construction) would otherwise
 * divide by zero when converting a value to a pixel row, and that is exactly how a `NaN` reaches a
 * path's `d` attribute. A small symmetric pad is fabricated instead, so a flat line draws at the
 * plot's vertical middle rather than collapsing.
 * @param values The plotted values alone, in any order.
 * @returns The range to scale against; `{ min: 0, max: 1 }` for an empty series.
 */
export const computeValueRange = (values: number[]): ChartValueRange => {
  if (values.length === 0) {
    return { min: 0, max: 1 }
  }
  const max = Math.max(...values)
  const min = Math.min(...values)
  if (max === min) {
    const pad = Math.max(Math.abs(max) * 0.1, 1)
    return { min: max - pad, max: max + pad }
  }
  return { min, max }
}

/**
 * Converts a raw value to its vertical pixel row inside the plot, against a given range.
 * @param value The raw value to place.
 * @param range The range to scale against, from {@link computeValueRange}.
 * @returns The value's y position in the SVG's user units.
 */
export const valueToY = (value: number, range: ChartValueRange): number => {
  const span = range.max - range.min
  return CHART_MARGIN.top + PLOT_HEIGHT - ((value - range.min) / span) * PLOT_HEIGHT
}

/**
 * Places every bucket at its pixel position. A single point is centred horizontally; two or more
 * are spread evenly across the plot width.
 * @param series The series to plot, already sorted (see {@link sortChartSeries}).
 * @param range The vertical range to scale against, from {@link computeValueRange}.
 * @returns One {@link ChartPlottedPoint} per bucket, in the same order.
 */
export const computeChartPoints = (series: ChartPoint[], range: ChartValueRange): ChartPlottedPoint[] => {
  const count = series.length
  return series.map((point, index) => {
    const x =
      count === 1
        ? CHART_MARGIN.left + PLOT_WIDTH / 2
        : CHART_MARGIN.left + (index / (count - 1)) * PLOT_WIDTH
    return { at: point.at, value: point.value, x, y: valueToY(point.value, range) }
  })
}

/**
 * Builds the line's SVG path data.
 * @param points The plotted points, in x order.
 * @returns The path's `d` attribute, or `null` when fewer than two points exist to draw a line
 * through — a line "through" one point has no direction to take, so it is not faked.
 */
export const buildLinePath = (points: ChartPlottedPoint[]): string | null => {
  if (points.length < 2) {
    return null
  }
  return points
    .map((point, index) => {
      return `${index === 0 ? 'M' : 'L'}${point.x},${point.y}`
    })
    .join(' ')
}

/**
 * Builds the filled area beneath the line, closed down to the plot's floor.
 * @param points The plotted points, in x order.
 * @param linePath The line's own path data, from {@link buildLinePath} — passed in rather than
 * recomputed, so the two paths can never disagree on which points they were built from.
 * @returns The area's `d` attribute, or `null` whenever the line itself is `null`.
 */
export const buildAreaPath = (points: ChartPlottedPoint[], linePath: string | null): string | null => {
  if (linePath === null || points.length < 2) {
    return null
  }
  const first = points[0]
  const last = points[points.length - 1]
  return `${linePath} L${last.x},${CHART_BASELINE_Y} L${first.x},${CHART_BASELINE_Y} Z`
}

/**
 * Selects the vertical axis's three gridlines: the range's max, midpoint and min. Labels are left
 * unformatted — the caller's `formatValue` is a component-level concern this module does not know
 * about.
 * @param range The vertical range, from {@link computeValueRange}.
 * @returns The three {@link ChartYTick}s, top to bottom.
 */
export const computeYTicks = (range: ChartValueRange): ChartYTick[] => {
  const mid = (range.max + range.min) / 2
  return [range.max, mid, range.min].map((value) => {
    return { y: valueToY(value, range), value }
  })
}

/**
 * Selects up to three horizontal axis labels: the first, middle and last bucket. A series of one
 * or two buckets collapses the duplicate indices via a `Set`, so it never labels the same point
 * twice.
 * @param points The plotted points, in x order.
 * @returns The selected {@link ChartXTick}s, left to right; empty when `points` is empty.
 */
export const computeXTicks = (points: ChartPlottedPoint[]): ChartXTick[] => {
  if (points.length === 0) {
    return []
  }
  const lastIndex = points.length - 1
  const midIndex = Math.round(lastIndex / 2)
  const indices = [...new Set([0, midIndex, lastIndex])]
  return indices.map((index, position) => {
    const point = points[index]
    const anchor: ChartTickAnchor = position === 0 ? 'start' : position === indices.length - 1 ? 'end' : 'middle'
    return { x: point.x, at: point.at, anchor }
  })
}

/**
 * Computes the hover readout's horizontal position, clamped so it never draws outside the viewBox.
 * @param point The currently hovered point, or `null` when nothing is hovered.
 * @returns The readout's x position; `0` when `point` is `null` (the readout is not rendered then).
 */
export const computeReadoutX = (point: ChartPlottedPoint | null): number => {
  if (point === null) {
    return 0
  }
  const half = CHART_READOUT_WIDTH / 2
  return Math.min(Math.max(point.x - half, 2), CHART_WIDTH - CHART_READOUT_WIDTH - 2)
}

/**
 * Computes the hover readout's vertical position: above the point by default, flipped below near
 * the plot's top edge so it never draws outside the viewBox.
 * @param point The currently hovered point, or `null` when nothing is hovered.
 * @returns The readout's y position; `0` when `point` is `null` (the readout is not rendered then).
 */
export const computeReadoutY = (point: ChartPlottedPoint | null): number => {
  if (point === null) {
    return 0
  }
  const above = point.y - CHART_READOUT_HEIGHT - 10
  return above < CHART_MARGIN.top ? point.y + 10 : above
}

/**
 * Finds the plotted point nearest a horizontal position, by simple x-distance — the pointer/touch
 * hover target is "nearest bucket", not "bucket under the exact pixel".
 * @param points The plotted points to search. The caller is expected to guard the empty case;
 * an empty array returns `0` rather than throwing.
 * @param userX The pointer's horizontal position, already converted to the SVG's user units.
 * @returns The index into `points` of the nearest point.
 */
export const findNearestPointIndex = (points: ChartPlottedPoint[], userX: number): number => {
  let nearestIndex = 0
  let nearestDistance = Number.POSITIVE_INFINITY
  points.forEach((point, index) => {
    const distance = Math.abs(point.x - userX)
    if (distance < nearestDistance) {
      nearestDistance = distance
      nearestIndex = index
    }
  })
  return nearestIndex
}
