/**
 * Pure geometry for {@link ../components/ui/UiLiveChart.vue | UiLiveChart}: decimation, a TIME
 * scale, gap-aware path building and a round-number ceiling, all plain functions over numbers.
 *
 * Unlike `chartGeometry` (which spaces buckets evenly by index, right for stored buckets on a fixed
 * step), a live series is positioned by its instants: a tab that was hidden for ten minutes leaves
 * a hole the line must not bridge, and an even spacing would hide it. A missing value (`null`) and
 * a gap longer than the series' own cadence both break the line; neither is ever drawn as zero.
 *
 * Nothing here touches Vue, i18n or the DOM.
 */

/** One reading of one series. `value` is `null` where the source had nothing to say (a gap, never a zero). */
export interface LiveChartPoint {
  /** The instant, a Unix epoch in milliseconds. */
  at: number
  /** The reading in the series' own unit, or `null` for none. */
  value: number | null
}

/** The span of time the horizontal axis covers. */
export interface LiveChartDomain {
  /** First instant, epoch milliseconds. */
  from: number
  /** Last instant, epoch milliseconds. */
  to: number
}

/** The values the vertical axis covers; `min` is always 0 for the non-negative quantities this panel plots. */
export interface LiveChartRange {
  /** Lowest value drawn. */
  min: number
  /** Highest value drawn. */
  max: number
}

/** Width of the drawing surface in user units; the SVG is stretched to the card by its viewBox. */
export const LIVE_CHART_WIDTH = 600
/** Height of the drawing surface in user units. */
export const LIVE_CHART_HEIGHT = 160
/** Gridline positions as fractions of the plot height, top to bottom; the plot draws them and the axis labels sit on them. */
export const LIVE_CHART_GRID_FRACTIONS: readonly number[] = [0, 0.5, 1]
/**
 * How many median cadences apart two neighbouring points may be before the line breaks between them.
 * Three tolerates one lost frame; more would bridge a hidden tab's silence.
 */
const GAP_FACTOR = 3

/**
 * Thins a series to at most `limit` points by averaging equal-sized runs, so a drawn path stays
 * cheap however long the history is. A run keeps its last instant; a run holding only gaps stays a
 * gap. A series already within the limit is returned as it is.
 * @param points The series, oldest first.
 * @param limit The most points to return; at least 2.
 * @returns The thinned series, oldest first.
 */
export const decimatePoints = (points: LiveChartPoint[], limit: number): LiveChartPoint[] => {
  if (points.length <= limit) {
    return points
  }

  const run = Math.ceil(points.length / limit)
  const thinned: LiveChartPoint[] = []
  for (let start = 0; start < points.length; start += run) {
    const slice = points.slice(start, start + run)
    const measured = slice.filter((point): point is { at: number; value: number } => {
      return point.value !== null
    })
    const last = slice[slice.length - 1]
    thinned.push({
      at: last.at,
      value:
        measured.length === 0
          ? null
          : measured.reduce((sum, point) => {
              return sum + point.value
            }, 0) / measured.length,
    })
  }
  return thinned
}

/**
 * Finds the cadence of a series: the median distance between neighbouring instants.
 * @param points The series, oldest first.
 * @returns The median step in milliseconds, or 0 with fewer than two points.
 */
export const medianStep = (points: LiveChartPoint[]): number => {
  if (points.length < 2) {
    return 0
  }
  const steps = points
    .slice(1)
    .map((point, index) => {
      return point.at - points[index].at
    })
    .sort((a, b) => {
      return a - b
    })
  return steps[Math.floor(steps.length / 2)]
}

/**
 * Rounds a value up to a number a person can read an axis in: 1, 2, 2.5, 5 or 10 times a power of ten.
 * @param value The largest value drawn; zero or less gives 1.
 * @returns The smallest such number that is at least `value`.
 */
export const niceCeiling = (value: number): number => {
  if (value <= 0) {
    return 1
  }
  const magnitude = 10 ** Math.floor(Math.log10(value))
  const step = [1, 2, 2.5, 5, 10]
    .map((multiple) => {
      return multiple * magnitude
    })
    .find((candidate) => {
      return candidate >= value
    })
  return Number((step ?? 10 * magnitude).toPrecision(12))
}

/**
 * Places an instant on the horizontal axis.
 * @param at The instant, epoch milliseconds.
 * @param domain The time span the axis covers.
 * @returns The x position in user units; the middle when the span is a single instant.
 */
export const timeToX = (at: number, domain: LiveChartDomain): number => {
  const span = domain.to - domain.from
  return span <= 0 ? LIVE_CHART_WIDTH / 2 : ((at - domain.from) / span) * LIVE_CHART_WIDTH
}

/**
 * Places a value on the vertical axis (larger is higher).
 * @param value The reading.
 * @param range The values the axis covers.
 * @returns The y position in user units.
 */
export const valueToLiveY = (value: number, range: LiveChartRange): number => {
  const span = range.max - range.min
  const clamped = Math.min(Math.max(value, range.min), range.max)
  return LIVE_CHART_HEIGHT - ((clamped - range.min) / span) * LIVE_CHART_HEIGHT
}

/**
 * Builds a series' line as SVG path data, starting a new sub-path after every gap.
 * @param points The series, oldest first.
 * @param domain The time span the axis covers.
 * @param range The values the axis covers.
 * @returns The `d` attribute, or `null` when no two neighbouring points are connected (a single
 * point has no line; it is drawn as a marker by the caller).
 */
export const buildLivePath = (
  points: LiveChartPoint[],
  domain: LiveChartDomain,
  range: LiveChartRange,
): string | null => {
  const limit = medianStep(points) * GAP_FACTOR
  const commands: string[] = []
  let previous: LiveChartPoint | null = null
  let connected = false

  for (const point of points) {
    if (point.value === null) {
      previous = null
      continue
    }

    const x = timeToX(point.at, domain).toFixed(1)
    const y = valueToLiveY(point.value, range).toFixed(1)
    const continues = previous !== null && limit > 0 && point.at - previous.at <= limit
    commands.push(`${continues ? 'L' : 'M'}${x},${y}`)
    connected = connected || continues
    previous = point
  }

  return connected ? commands.join(' ') : null
}

/**
 * Finds the index of the point nearest an instant that has a reading.
 * @param points The series, oldest first.
 * @param at The instant to look near, epoch milliseconds.
 * @returns The index, or `null` when no point holds a reading.
 */
export const nearestMeasuredIndex = (points: LiveChartPoint[], at: number): number | null => {
  let best: number | null = null
  let bestDistance = Number.POSITIVE_INFINITY
  points.forEach((point, index) => {
    if (point.value === null) {
      return
    }
    const distance = Math.abs(point.at - at)
    if (distance < bestDistance) {
      bestDistance = distance
      best = index
    }
  })
  return best
}

/** What is drawn for one series: its path data and, when it holds a single reading, where to mark it. */
export interface LiveChartLineShape {
  /** The path data, or `null` when no two neighbouring readings are connected. */
  path: string | null
  /** The place of a lone reading in user units, or `null`; one reading cannot make a line. */
  lone: { x: number; y: number } | null
}

/**
 * Finds the time span covered by a set of series, so hiding one line never moves the axis.
 * @param seriesPoints The readings of every series, each oldest first.
 * @returns The span from the earliest first instant to the latest last instant, or `null` when no series has a point.
 */
export const liveChartDomain = (seriesPoints: LiveChartPoint[][]): LiveChartDomain | null => {
  const ends = seriesPoints.flatMap((points) => {
    return points.length === 0 ? [] : [points[0].at, points[points.length - 1].at]
  })
  return ends.length === 0 ? null : { from: Math.min(...ends), to: Math.max(...ends) }
}

/**
 * Finds the largest reading of a set of series; a gap counts as zero here because it only sizes the axis.
 * @param seriesPoints The readings of every series.
 * @returns The largest value, or 0 when there is none.
 */
export const largestReading = (seriesPoints: LiveChartPoint[][]): number => {
  return Math.max(
    0,
    ...seriesPoints.flatMap((points) => {
      return points.map((point) => {
        return point.value ?? 0
      })
    }),
  )
}

/**
 * Shapes one series for drawing: thinned, then turned into path data, with a marker position when
 * it holds exactly one reading.
 * @param points The series, oldest first.
 * @param domain The time span the axis covers.
 * @param range The values the axis covers.
 * @param limit The most points the series is thinned to.
 * @returns The path and the lone-reading marker.
 */
export const shapeLiveLine = (
  points: LiveChartPoint[],
  domain: LiveChartDomain,
  range: LiveChartRange,
  limit: number,
): LiveChartLineShape => {
  const thinned = decimatePoints(points, limit)
  const measured = thinned.filter((point) => {
    return point.value !== null
  })
  const only = measured.length === 1 ? measured[0] : null
  return {
    path: buildLivePath(thinned, domain, range),
    lone:
      only === null || only.value === null
        ? null
        : {
            x: timeToX(only.at, domain),
            y: LIVE_CHART_HEIGHT - (only.value / range.max) * LIVE_CHART_HEIGHT,
          },
  }
}
