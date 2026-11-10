import { format } from 'date-fns'
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref, type ShallowRef } from 'vue'
import { computeXTicks, type ChartPlottedPoint, type ChartTickAnchor } from '../utils/chartGeometry'
import { chartXPercent } from '../utils/chartView'

/** The least blank space, in CSS pixels, kept between two neighbouring time labels. */
const TICK_GAP = 12

/**
 * Safety margin on a canvas-measured label width. A canvas cannot switch on `tabular-nums`, whose
 * digits are the widest of a font's digit forms, so the measurement is padded rather than trusted
 * to the pixel.
 */
const TICK_WIDTH_MARGIN = 1.06

/** One time label ready to place: its user-space column, text and anchor. */
export interface ChartTimeLabel {
  /** User-space x of the tick the label belongs to. */
  x: number
  /** Ready-to-render text. */
  label: string
  /** Which side of the tick the label extends to. */
  anchor: ChartTickAnchor
}

/** What {@link useChartTimeAxis} hands the axis component. */
export interface ChartTimeAxis {
  /** The labels to render: the richest layout whose labels do not collide in the measured row. */
  labels: ComputedRef<ChartTimeLabel[]>
}

/**
 * Chooses the time labels of a chart so they never collide (WCAG 1.4.10 reflow, 1.4.12 text spacing).
 *
 * Three `1 Sep, 04:00` labels need roughly 270 px of row; the plot of a 320 px phone offers about
 * 230, and the 1.4.12 spacing override (letter-spacing 0.12em) widens them by another fifth even at
 * 390. The labels are absolutely positioned, so nothing pushes them apart — they simply print over
 * each other. This MEASURES: it asks a canvas (set to the row's own computed font, letter-spacing and
 * word-spacing) how wide each candidate label is, lays the candidates out at their anchors in the
 * row's measured width, and takes the first layout whose neighbours keep a gap of at least
 * {@link TICK_GAP}. The candidates run from richest to poorest: all three labels; first and last
 * only (the middle one is the first to go, the ends frame the period); the same with the date left
 * out when every kept tick falls on one calendar day (a day's date repeated three times says
 * nothing); finally the last tick alone. A ResizeObserver on the row and on the rendered labels
 * re-runs the choice on a resize, a font load or a spacing override — the layout is a pure function
 * of those measurements, so it cannot oscillate.
 * @param row The row holding the time labels (the component's template ref), measured for its width and for the font the labels use.
 * @param points The plotted points the labels point at.
 * @param formatAt Formats a bucket's instant in the panel's current language.
 * @returns The labels to render.
 */
export const useChartTimeAxis = (
  row: Readonly<ShallowRef<HTMLElement | null>>,
  points: ComputedRef<ChartPlottedPoint[]>,
  formatAt: (at: number) => string,
): ChartTimeAxis => {
  /** The measured width of {@link row} in CSS pixels; `0` until the first measurement. */
  const rowWidth: Ref<number> = ref(0)

  /**
   * Bumped by every observer callback, so {@link labels} (which reads fonts and spacing off the live
   * DOM, something Vue cannot track) is recomputed when a font loads or a spacing override lands.
   */
  const measureEpoch: Ref<number> = ref(0)

  /** Watches the row and its labels for any change of size. `null` outside a browser. */
  let observer: ResizeObserver | null = null

  /** What was measured last (width and typography), so an unchanged re-measurement is ignored. */
  let lastSignature = ''

  /** Shared off-screen canvas context used to measure label text; created on first use. */
  let measureContext: CanvasRenderingContext2D | null = null

  /**
   * The rendered width of a label under the row's current typography.
   * @param label The text to measure.
   * @returns The width in CSS pixels, padded by {@link TICK_WIDTH_MARGIN}.
   */
  const measureLabel = (label: string): number => {
    const element = row.value
    if (element === null) {
      return 0
    }
    const style = getComputedStyle(element)
    measureContext ??= document.createElement('canvas').getContext('2d')
    if (measureContext === null) {
      // No canvas: assume a generous 0.65 em per glyph rather than guess the labels fit.
      return label.length * (parseFloat(style.fontSize) || 12) * 0.65
    }
    measureContext.font = style.font
    measureContext.letterSpacing = style.letterSpacing === 'normal' ? '0px' : style.letterSpacing
    measureContext.wordSpacing = style.wordSpacing === 'normal' ? '0px' : style.wordSpacing
    return measureContext.measureText(label).width * TICK_WIDTH_MARGIN
  }

  /**
   * Whether a set of labels keeps {@link TICK_GAP} between every neighbouring pair in the row.
   * @param candidate The labels, left to right.
   * @param width The row's measured width in CSS pixels.
   * @returns `true` when no two labels touch and none leaves the row.
   */
  const labelsFit = (candidate: ChartTimeLabel[], width: number): boolean => {
    let previousEnd = -Infinity
    for (const tick of candidate) {
      const labelWidth = measureLabel(tick.label)
      const anchorX = (parseFloat(chartXPercent(tick.x)) / 100) * width
      const start = anchorX - (tick.anchor === 'start' ? 0 : tick.anchor === 'middle' ? labelWidth / 2 : labelWidth)
      if (start < previousEnd + TICK_GAP || start + labelWidth > width + 0.5) {
        return false
      }
      previousEnd = start + labelWidth
    }
    return true
  }

  /**
   * The time labels along the bottom edge: the richest candidate layout whose labels do not collide
   * in the measured row. Before the first measurement the full set renders, and the choice settles
   * on the next frame.
   */
  const labels: ComputedRef<ChartTimeLabel[]> = computed(() => {
    // Read so a font load or spacing override (observed, not reactive) re-evaluates this.
    void measureEpoch.value
    const ticks = computeXTicks(points.value)
    const full: ChartTimeLabel[] = ticks.map((tick) => {
      return { x: tick.x, label: formatAt(tick.at), anchor: tick.anchor }
    })
    const width = rowWidth.value
    if (width === 0 || full.length < 2) {
      return full
    }
    const ends = [full[0], full[full.length - 1]].map((tick, position) => {
      return { ...tick, anchor: position === 0 ? ('start' as const) : ('end' as const) }
    })
    const sameDay =
      new Set(
        ticks.map((tick) => {
          return new Date(tick.at).toDateString()
        }),
      ).size === 1
    /**
     * Replaces each label's text with its time of day.
     * @param shown The labels to shorten.
     * @returns The same labels reading `HH:mm` only.
     */
    const timeOnly = (shown: ChartTimeLabel[]): ChartTimeLabel[] => {
      return shown.map((tick) => {
        const at = ticks.find((candidate) => {
          return candidate.x === tick.x
        })?.at
        return at === undefined ? tick : { ...tick, label: format(new Date(at), 'HH:mm') }
      })
    }
    const candidates: ChartTimeLabel[][] = [full, ends]
    if (sameDay) {
      candidates.push(timeOnly(full), timeOnly(ends))
    }
    const chosen = candidates.find((shown) => {
      return labelsFit(shown, width)
    })
    return chosen ?? [{ ...full[full.length - 1], anchor: 'end' as const }]
  })

  /**
   * Re-measures the row and bumps {@link measureEpoch}; the observer's callback.
   * @returns Nothing.
   */
  const remeasure = (): void => {
    const element = row.value
    const style = element === null ? null : getComputedStyle(element)
    const signature = `${String(element?.clientWidth ?? 0)}|${style?.font ?? ''}|${style?.letterSpacing ?? ''}|${style?.wordSpacing ?? ''}`
    // Only a real change re-runs the layout: a re-observed element reports its size once at once,
    // and treating that as news would loop (observe, measure, re-render, observe).
    if (signature === lastSignature) {
      return
    }
    lastSignature = signature
    rowWidth.value = element?.clientWidth ?? 0
    measureEpoch.value += 1
  }

  onMounted((): void => {
    if (typeof ResizeObserver === 'undefined') {
      return
    }
    observer = new ResizeObserver(remeasure)
    void document.fonts?.ready.then(remeasure)
  })

  onBeforeUnmount((): void => {
    observer?.disconnect()
    observer = null
  })

  // The row exists only once the series is non-empty, and the labels are replaced as the chosen
  // layout changes; observe whatever is rendered now, and measure immediately.
  watch(
    [
      row,
      (): string => {
        return labels.value
          .map((tick) => {
            return tick.label
          })
          .join('|')
      },
    ],
    (): void => {
      const active = observer
      const element = row.value
      if (active === null || element === null) {
        return
      }
      active.disconnect()
      active.observe(element)
      element.querySelectorAll('span').forEach((label) => {
        active.observe(label)
      })
      remeasure()
    },
    { flush: 'post' },
  )

  return { labels }
}
