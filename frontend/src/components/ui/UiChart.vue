<script setup lang="ts">
/**
 * Inline SVG line-and-area chart for a time series — the panel's monitoring
 * screens plot CPU, memory, disk and network rate buckets through this one
 * primitive. It is the kit's only hand-drawn, non-icon SVG (rules/vue.md:
 * "icon SVG comes only from lucide via `UiIcon`; `UiChart` is the single
 * non-icon SVG site") — a data plot has no lucide equivalent to reach for.
 *
 * Behaviour: a filled area under a line, axis ticks on both edges, and a
 * hover readout showing the pointed-at bucket's value, formatted through the
 * caller's `formatValue` when given and through a plain one-decimal rounding
 * otherwise. An empty `series` — the ordinary state before the first bucket
 * arrives, not an error — renders {@link UiEmptyState} instead of an SVG with
 * nothing to plot. A single-point series draws one marker and no line: a
 * line drawn "through" one point has no direction to take, so it is not
 * faked. Both cases, and a perfectly flat series, are handled by construction
 * rather than by catching a `NaN`: {@link valueRange} always returns a
 * non-zero span, so no divide-by-zero can reach a path's `d` attribute.
 *
 * Hover readout: drawn as a group INSIDE the same SVG, in the chart's own
 * user-coordinate space, clamped to stay inside the viewBox. This is a
 * deliberately different choice from `UiDropdown`'s panel, which teleports to
 * `body` and positions itself in viewport pixels — that escape hatch exists
 * there because a menu has to outlive an ancestor's `overflow: auto` clipping
 * a table imposes. A chart is not inside a scrolling ancestor and the readout
 * never has to leave the box it describes, so teleporting it would add a
 * measuring/repositioning machine for a problem this component does not have.
 *
 * Accessibility: the SVG is presentation-only (`aria-hidden`) — a sighted
 * pointer enhancement, not the data's home. Every bucket is also rendered as
 * an ordinary `sr-only` HTML `<table>` (a legitimate raw element here, same
 * exception `UiTable` relies on: `components/ui/` implements the primitives
 * that make raw markup unnecessary everywhere else). A screen-reader user
 * therefore gets the COMPLETE series as real cells, not a lossy one-sentence
 * summary standing in for a picture. Pointer events (`pointermove`/
 * `pointerleave`), not mouse events, drive the hover state: besides working
 * for touch and pen as well as a mouse, `mouseleave` requires a paired
 * `focus`/`blur` handler under this kit's accessibility lint
 * (`vuejs-accessibility/mouse-events-have-key-events`), which would mean
 * wiring focus handling onto an element this component deliberately keeps out
 * of the tab order.
 *
 * Look: ONE series, drawn in the accent (`--ac`, a graphic, never a surface under text) with a
 * soft fade beneath it, on faint gridlines. The gridlines are `--b1`, about 1.2:1 against a panel
 * — decoration only; the values are carried by the labels, which are `text-text-muted` (5.36:1 on a
 * panel, 5.03:1 on the raised surface the readout sits on, in dark; 5.41 / 4.91 in light). The line
 * against a panel is 4.81:1 dark and 3.89:1 light, over the 3:1 a graphic needs. Strokes use
 * `vector-effect: non-scaling-stroke`, because the viewBox is scaled to the card's width and a 2 px
 * line would otherwise thin to about 1 px on a phone.
 *
 * **The axis labels are HTML beside the SVG, not `<text>` inside it.** The SVG is stretched to the
 * card's width, and text drawn in its coordinate space shrinks with it: at 390 px the ticks came
 * out near 6 px. The labels are therefore ordinary `text-xs` elements that keep their size at any
 * width, positioned by PERCENTAGES of the plotted box so they still land on their gridlines. The
 * y labels share one grid cell (so the gutter is as wide as the widest label, whatever the unit)
 * and are placed down it by `top`; the x labels sit in a row under the plot and are shifted by
 * their anchor. Because the left margin and the bottom strip of the geometry module's coordinate
 * space now belong to those labels, the viewBox is CROPPED to the plot ({@link VIEW}) rather than
 * covering the geometry module's whole canvas; the geometry module is unchanged.
 * Both label groups are `aria-hidden`, like the SVG: the readings table carries the data.
 * The hover readout is still drawn inside the SVG and still scales with it.
 *
 * **The header reading is load-bearing.** The latest value's `<span>` carries `font-mono` on
 * purpose: `e2e/monitoring/monitoring.spec.ts` locates the reading as
 * `.ui-chart span.font-mono` and asserts its exact text (e.g. "45.5 %"), so the class is the
 * spec's contract, not only a style. It also suits the content — a figure read against others
 * wants fixed-width digits, as in the kit's other numeric readings (`UiMeter`, `UiPageHeading`).
 * Keep it the ONLY `font-mono` span in the chart's root, and keep its text to the formatted value
 * with its unit and nothing else.
 *
 * **The time labels never collide (WCAG 1.4.10 reflow, 1.4.12 text spacing).** Three
 * `1 Sep, 04:00` labels need roughly 270 px of row; the plot of a 320 px phone offers about 230, and
 * the 1.4.12 spacing override (letter-spacing 0.12em) widens them by another fifth even at 390. The
 * labels are absolutely positioned, so nothing pushes them apart — they simply print over each
 * other. {@link xTicks} therefore MEASURES: it asks a canvas (set to the row's own computed font,
 * letter-spacing and word-spacing) how wide each candidate label is, lays the candidates out at
 * their anchors in the row's measured width, and takes the first layout whose neighbours keep a
 * gap of at least {@link TICK_GAP}. The candidates run from richest to poorest: all three labels;
 * first and last only (the middle one is the first to go, the ends frame the period); the same
 * with the date left out when every kept tick falls on one calendar day (a day's date repeated
 * three times says nothing); finally the last tick alone. A ResizeObserver on the row and on the
 * rendered labels re-runs the choice on a resize, a font load or a spacing override — the layout is
 * a pure function of those measurements, so it cannot oscillate.
 *
 * **A real scale.** The vertical axis used to be labelled with the series' own minimum, midpoint
 * and maximum, so a gridline read `72.00 / 41.00 / 10.00` — true, but a scale nobody can read a
 * third value off. The range is now widened to round steps ({@link niceRange}: 1, 2, 2.5 or 5 times
 * a power of ten), so the three gridlines fall on round numbers (`100 / 50 / 0`). The widening
 * never crosses zero when the data does not, so the "never below zero" lesson of
 * {@link computeValueRange} still holds.
 */
import { format } from 'date-fns'
import { computed, onBeforeUnmount, onMounted, ref, useId, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AppLocale } from '../../types/app'
import { formatChartInstant } from '../../utils/formatChartInstant'
import {
  buildAreaPath,
  buildLinePath,
  CHART_BASELINE_Y as BASELINE_Y,
  CHART_MARGIN as MARGIN,
  CHART_READOUT_HEIGHT as READOUT_HEIGHT,
  CHART_READOUT_WIDTH as READOUT_WIDTH,
  CHART_WIDTH,
  computeChartPoints,
  computeReadoutX,
  computeReadoutY,
  computeValueRange,
  computeXTicks,
  computeYTicks,
  findNearestPointIndex,
  sortChartSeries,
  type ChartPlottedPoint,
  type ChartTickAnchor,
  type ChartValueRange,
} from '../../utils/chartGeometry'
import UiEmptyState from './UiEmptyState.vue'
import UiIcon from './UiIcon.vue'

/**
 * The part of the geometry module's coordinate space the SVG actually shows: the plot itself, a
 * few units of breathing room on the left for the first marker, and nothing of the left margin or
 * the bottom strip, which the HTML axis labels occupy instead. See the component doc.
 */
const VIEW = {
  x: MARGIN.left - 4,
  y: 0,
  width: CHART_WIDTH - (MARGIN.left - 4),
  height: BASELINE_Y + 4,
}

/**
 * The horizontal shift that lines a label up with its tick by its anchor: a `start` label begins at
 * the tick, an `end` label finishes on it, a `middle` label is centred on it.
 */
const ANCHOR_SHIFT: Record<ChartTickAnchor, string> = {
  start: '0',
  middle: '-50%',
  end: '-100%',
}

/** The least blank space, in CSS pixels, kept between two neighbouring time labels. */
const TICK_GAP = 12

/**
 * Safety margin on a canvas-measured label width. A canvas cannot switch on `tabular-nums`, whose
 * digits are the widest of a font's digit forms, so the measurement is padded rather than trusted
 * to the pixel.
 */
const TICK_WIDTH_MARGIN = 1.06

/** One measured value at one instant — the shape every bucket source (CPU, memory, disk…) sends. */
export interface UiChartPoint {
  /** When the value was recorded, as a Unix epoch in milliseconds. */
  at: number
  /** The measured value, in the chart's `unit`. */
  value: number
}

/** Props accepted by {@link UiChart}. */
const props = defineProps<{
  /** The bucketed series to plot, oldest first or not — the component sorts defensively. */
  series: UiChartPoint[]
  /** Name of the plotted metric, already translated by the caller (e.g. "CPU"). */
  label: string
  /**
   * Unit the values are in (e.g. "%", "MiB/s"). Omitted for a dimensionless metric — a load
   * average is a bare number, and appending a word to it would invent a unit it does not have;
   * the readings then render alone and the readings table says so in its caption.
   */
  unit?: string
  /** Optional formatter for a raw value; falls back to a plain one-decimal rounding when omitted. */
  formatValue?: (value: number) => string
}>()

const { t, locale } = useI18n()

/** Index into {@link points} the pointer is currently over; `null` when nothing is hovered. */
const hoveredIndex: Ref<number | null> = ref(null)
/** The rendered `<svg>`, measured on every pointer move to convert client pixels to user units. */
const svgElement: Ref<SVGSVGElement | null> = ref(null)
/**
 * Unique id for this chart's area gradient. A page draws six charts at once, and a shared literal
 * id would make every one of them resolve `url(#…)` to the first gradient in the document. Vue's
 * generated id is stripped to characters that are safe inside a `url()` reference.
 */
const gradientId: string = `ui-chart-fade-${useId().replace(/[^A-Za-z0-9_-]/g, '_')}`

/**
 * The series sorted by time. Defensive rather than trusting the caller's order: a bucket source
 * that emitted out of sequence would otherwise draw a path that zig-zags backwards, which reads as
 * a rendering bug rather than as the data it is. Delegates to {@link sortChartSeries}.
 */
const sortedSeries: ComputedRef<UiChartPoint[]> = computed(() => {
  return sortChartSeries(props.series)
})

/**
 * The vertical range the plot scales against — see {@link computeValueRange} for why it is never
 * zero-width, and {@link niceRange} for why it is then widened to round values.
 */
const valueRange: ComputedRef<ChartValueRange> = computed(() => {
  return niceRange(
    computeValueRange(
      sortedSeries.value.map((point) => {
        return point.value
      }),
    ),
  )
})

/** Every bucket with its pixel position. A single point is centred; two or more are spread evenly. */
const points: ComputedRef<ChartPlottedPoint[]> = computed(() => {
  return computeChartPoints(sortedSeries.value, valueRange.value)
})

/** The line's path data, or `null` when fewer than two points exist to draw a line through. */
const linePath: ComputedRef<string | null> = computed(() => {
  return buildLinePath(points.value)
})

/** The filled area beneath the line, closed down to the plot's floor. `null` alongside the line. */
const areaPath: ComputedRef<string | null> = computed(() => {
  return buildAreaPath(points.value, linePath.value)
})

/**
 * Three horizontal gridlines/labels: the range's max, midpoint and min. {@link computeYTicks}
 * selects the values and their pixel rows; the label text is formatted here, since it depends on
 * the caller's own `formatValue` prop, which the geometry module deliberately knows nothing about.
 */
const yTicks: ComputedRef<{ y: number; label: string }[]> = computed(() => {
  return computeYTicks(valueRange.value).map((tick) => {
    return { y: tick.y, label: formatValueLabel(tick.value) }
  })
})

/** One time label ready to place: its user-space column, text and anchor. */
interface XTickLabel {
  /** User-space x of the tick the label belongs to. */
  x: number
  /** Ready-to-render text. */
  label: string
  /** Which side of the tick the label extends to. */
  anchor: ChartTickAnchor
}

/** The row holding the time labels, measured for its width and for the font the labels use. */
const xAxisRow: Ref<HTMLElement | null> = ref(null)

/** The measured width of {@link xAxisRow} in CSS pixels; `0` until the first measurement. */
const xAxisWidth: Ref<number> = ref(0)

/**
 * Bumped by every observer callback, so {@link xTicks} (which reads fonts and spacing off the live
 * DOM, something Vue cannot track) is recomputed when a font loads or a spacing override lands.
 */
const measureEpoch: Ref<number> = ref(0)

/** Watches the row and its labels for any change of size. `null` outside a browser. */
let axisObserver: ResizeObserver | null = null

/** What was measured last (width and typography), so an unchanged re-measurement is ignored. */
let lastAxisSignature = ''

/** Shared off-screen canvas context used to measure label text; created on first use. */
let measureContext: CanvasRenderingContext2D | null = null

/**
 * The time labels along the bottom edge: the richest candidate layout whose labels do not collide
 * in the measured row (see the component doc). Before the first measurement the full set renders,
 * and the choice settles on the next frame.
 */
const xTicks: ComputedRef<XTickLabel[]> = computed(() => {
  // Read so a font load or spacing override (observed, not reactive) re-evaluates this.
  void measureEpoch.value
  const ticks = computeXTicks(points.value)
  const full: XTickLabel[] = ticks.map((tick) => {
    return { x: tick.x, label: formatAt(tick.at), anchor: tick.anchor }
  })
  const rowWidth = xAxisWidth.value
  if (rowWidth === 0 || full.length < 2) {
    return full
  }
  const ends = [full[0], full[full.length - 1]].map((tick, position) => {
    return { ...tick, anchor: position === 0 ? ('start' as const) : ('end' as const) }
  })
  const sameDay = new Set(
    ticks.map((tick) => {
      return new Date(tick.at).toDateString()
    }),
  ).size === 1
  /**
   * Replaces each label's text with its time of day.
   * @param labels The labels to shorten.
   * @returns The same labels reading `HH:mm` only.
   */
  const timeOnly = (labels: XTickLabel[]): XTickLabel[] => {
    return labels.map((tick) => {
      const at = ticks.find((candidate) => {
        return candidate.x === tick.x
      })?.at
      return at === undefined ? tick : { ...tick, label: format(new Date(at), 'HH:mm') }
    })
  }
  const candidates: XTickLabel[][] = [full, ends]
  if (sameDay) {
    candidates.push(timeOnly(full), timeOnly(ends))
  }
  const chosen = candidates.find((labels) => {
    return labelsFit(labels, rowWidth)
  })
  return chosen ?? [{ ...full[full.length - 1], anchor: 'end' as const }]
})

/** The plotted point currently under the pointer, or `null` when nothing is hovered. */
const hoveredPoint: ComputedRef<ChartPlottedPoint | null> = computed(() => {
  const index = hoveredIndex.value
  return index === null ? null : (points.value[index] ?? null)
})

/** The hover readout's horizontal position, clamped so it never draws outside the viewBox. */
const readoutX: ComputedRef<number> = computed(() => {
  return computeReadoutX(hoveredPoint.value)
})

/** The hover readout's vertical position: above the point by default, flipped below near the top edge. */
const readoutY: ComputedRef<number> = computed(() => {
  return computeReadoutY(hoveredPoint.value)
})

/** The most recent bucket's value, formatted with its unit, for the header line. `null` when empty. */
const latestFormatted: ComputedRef<string | null> = computed(() => {
  const plotted = points.value
  const last = plotted[plotted.length - 1]
  return last === undefined ? null : formatValueWithUnit(last.value)
})

/**
 * The readings table's caption. Two messages rather than one with an empty hole, because the
 * grammar differs per language: dropping the unit from "Показания {label}, в {unit}" leaves a
 * dangling "в", so a unitless chart gets its own complete sentence in each locale.
 */
const tableCaption: ComputedRef<string> = computed(() => {
  return props.unit === undefined
    ? t('app.chart.tableCaptionNoUnit', { label: props.label })
    : t('app.chart.tableCaption', { label: props.label, unit: props.unit })
})

/**
 * Widens a range to round step boundaries so the axis reads in round numbers.
 *
 * The step is the smallest of 1, 2, 2.5, 5 or 10 times a power of ten that is at least half the
 * span, since the axis has two intervals (max, midpoint, min). The minimum is floored and the
 * maximum ceiled to that step, so a non-negative series stays non-negative and a range whose
 * midpoint is the middle gridline stays a multiple of the step. Results are rounded to twelve
 * significant digits to shed floating-point residue such as `0.30000000000000004`.
 * @param range The data's own range from {@link computeValueRange}, always of non-zero span.
 * @returns A range of the same shape that contains it and lands on round values.
 */
const niceRange = (range: ChartValueRange): ChartValueRange => {
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
  return { min: tidy(Math.floor(range.min / step) * step), max: tidy(Math.ceil(range.max / step) * step) }
}

/**
 * The formatter used when the caller supplies none: one decimal place, with a trailing ".0"
 * dropped so a whole number reads as one.
 * @param value The raw value to format.
 * @returns The value rounded to at most one decimal place.
 */
const defaultFormatValue = (value: number): string => {
  const rounded = Math.round(value * 10) / 10
  return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1)
}

/**
 * Formats a raw value alone, through the caller's `formatValue` when given.
 * @param value The raw value to format.
 * @returns The formatted value, without its unit.
 */
const formatValueLabel = (value: number): string => {
  return (props.formatValue ?? defaultFormatValue)(value)
}

/**
 * Formats a raw value with its unit appended — the header reading and the hover readout both
 * need the unit; the axis ticks deliberately do not, since three repeats of it down one column
 * would be noise the header already said once. A unitless chart renders the bare value: there is
 * nothing true to append.
 * @param value The raw value to format.
 * @returns The formatted value, followed by {@link props}'s `unit` when the metric has one.
 */
const formatValueWithUnit = (value: number): string => {
  const formatted = formatValueLabel(value)
  return props.unit === undefined ? formatted : `${formatted} ${props.unit}`
}

/**
 * Formats a bucket's instant for the axis ticks and the hover readout alike, in the panel's
 * current language.
 *
 * The locale comes from the same i18n instance every other string in this component reads —
 * which `stores/locale.ts` feeds (rules/vue.md: one source of truth) — so the axis can never
 * speak a different language from the caption beside it. This component once called date-fns
 * directly with no locale, and a Russian screen read `8 Sep` on every axis.
 * @param at Unix epoch in milliseconds.
 * @returns A short day/time label.
 */
const formatAt = (at: number): string => {
  // The i18n instance is created over AppLocale and fed only from the locale store, so its
  // current value is one of the panel's own locales by construction.
  return formatChartInstant(at, locale.value as AppLocale)
}

/**
 * A user-space x position as a percentage of the visible plot's width.
 * @param x A user-space x, as in {@link ChartPlottedPoint}.
 * @returns The CSS percentage to position an HTML label with.
 */
const xPercent = (x: number): string => {
  return `${((x - VIEW.x) / VIEW.width) * 100}%`
}

/**
 * A user-space y position as a percentage of the visible plot's height.
 * @param y A user-space y, as in {@link ChartPlottedPoint}.
 * @returns The CSS percentage to position an HTML label with.
 */
const yPercent = (y: number): string => {
  return `${(y / VIEW.height) * 100}%`
}

/**
 * The rendered width of a label under the row's current typography.
 * @param label The text to measure.
 * @returns The width in CSS pixels, padded by {@link TICK_WIDTH_MARGIN}.
 */
const measureLabel = (label: string): number => {
  const row = xAxisRow.value
  if (row === null) {
    return 0
  }
  const style = getComputedStyle(row)
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
 * @param labels The labels, left to right.
 * @param rowWidth The row's measured width in CSS pixels.
 * @returns `true` when no two labels touch and none leaves the row.
 */
const labelsFit = (labels: XTickLabel[], rowWidth: number): boolean => {
  let previousEnd = -Infinity
  for (const tick of labels) {
    const width = measureLabel(tick.label)
    const anchorX = (parseFloat(xPercent(tick.x)) / 100) * rowWidth
    const start = anchorX - (tick.anchor === 'start' ? 0 : tick.anchor === 'middle' ? width / 2 : width)
    if (start < previousEnd + TICK_GAP || start + width > rowWidth + 0.5) {
      return false
    }
    previousEnd = start + width
  }
  return true
}

/**
 * Re-measures the row and bumps {@link measureEpoch}; the observer's callback.
 * @returns Nothing.
 */
const remeasureAxis = (): void => {
  const row = xAxisRow.value
  const style = row === null ? null : getComputedStyle(row)
  const signature = `${String(row?.clientWidth ?? 0)}|${style?.font ?? ''}|${style?.letterSpacing ?? ''}|${style?.wordSpacing ?? ''}`
  // Only a real change re-runs the layout: a re-observed element reports its size once at once,
  // and treating that as news would loop (observe, measure, re-render, observe).
  if (signature === lastAxisSignature) {
    return
  }
  lastAxisSignature = signature
  xAxisWidth.value = row?.clientWidth ?? 0
  measureEpoch.value += 1
}

/**
 * Tracks the pointer across the chart and selects the nearest bucket by horizontal distance.
 * Pointer events, not mouse events, so touch and pen hover the same way a mouse does.
 * @param event The native pointer-move event.
 * @returns Nothing; updates {@link hoveredIndex} synchronously.
 */
const onPointerMove = (event: PointerEvent): void => {
  const svg = svgElement.value
  const plotted = points.value
  if (svg === null || plotted.length === 0) {
    return
  }
  const rect = svg.getBoundingClientRect()
  if (rect.width === 0) {
    return
  }
  // The viewBox is stretched to fill the element's rendered box (preserveAspectRatio="none"), so
  // client pixels convert to user units with one linear scale factor.
  const userX = VIEW.x + (event.clientX - rect.left) * (VIEW.width / rect.width)
  hoveredIndex.value = findNearestPointIndex(plotted, userX)
}

/**
 * Clears the hover state once the pointer leaves the chart.
 * @returns Nothing; updates {@link hoveredIndex} synchronously.
 */
const onPointerLeave = (): void => {
  hoveredIndex.value = null
}

onMounted((): void => {
  if (typeof ResizeObserver === 'undefined') {
    return
  }
  axisObserver = new ResizeObserver(remeasureAxis)
  void document.fonts?.ready.then(remeasureAxis)
})

onBeforeUnmount((): void => {
  axisObserver?.disconnect()
  axisObserver = null
})

// The row exists only once the series is non-empty, and the labels are replaced as the chosen
// layout changes; observe whatever is rendered now, and measure immediately.
watch(
  [
    xAxisRow,
    (): string => {
      return xTicks.value
        .map((tick) => {
          return tick.label
        })
        .join('|')
    },
  ],
  (): void => {
    const observer = axisObserver
    const row = xAxisRow.value
    if (observer === null || row === null) {
      return
    }
    observer.disconnect()
    observer.observe(row)
    row.querySelectorAll('span').forEach((label) => {
      observer.observe(label)
    })
    remeasureAxis()
  },
  { flush: 'post' },
)
</script>

<template>
  <div class="ui-chart">
    <UiEmptyState v-if="points.length === 0" :title="label" :description="t('app.chart.emptyDescription')">
      <template #icon>
        <UiIcon name="pulse" />
      </template>
    </UiEmptyState>

    <template v-else>
      <div class="mb-3 flex items-baseline justify-between gap-3">
        <span
          class="font-display text-xs font-extrabold tracking-caps text-text-secondary uppercase wrap-anywhere"
          >{{ label }}</span
        >
        <span
          v-if="latestFormatted !== null"
          class="font-mono text-lg font-semibold text-text-primary tabular-nums wrap-anywhere"
          >{{ latestFormatted }}</span
        >
      </div>

      <div class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-2">
        <!-- Decorative, like the SVG: the same data is available in full, as real table cells,
             below. The three labels share ONE grid cell, so the column is as wide as the widest
             of them, and `top` as a percentage of that cell (the plot's height) puts each on its
             gridline; `self-start` keeps each label its own height, so `translate: -50%` centres
             it on the line instead of on a stretched box. -->
        <div class="col-start-1 row-start-1 grid" aria-hidden="true">
          <span
            v-for="tick in yTicks"
            :key="`y-${tick.y}`"
            class="ui-chart-axis-label relative col-start-1 row-start-1 self-start justify-self-end text-xs whitespace-nowrap text-text-muted tabular-nums"
            :style="{ top: yPercent(tick.y), translate: '0 -50%' }"
            >{{ tick.label }}</span
          >
        </div>

        <svg
          ref="svgElement"
          :viewBox="`${VIEW.x} ${VIEW.y} ${VIEW.width} ${VIEW.height}`"
          preserveAspectRatio="none"
          class="ui-chart-svg col-start-2 row-start-1 h-auto w-full touch-pan-y overflow-visible"
          aria-hidden="true"
          @pointermove="onPointerMove"
          @pointerleave="onPointerLeave"
        >
          <defs>
            <linearGradient :id="gradientId" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" class="ui-chart-fade-top" />
              <stop offset="1" class="ui-chart-fade-bottom" />
            </linearGradient>
          </defs>

          <g v-for="(tick, index) in yTicks" :key="`y-${tick.y}`">
            <line
              :x1="MARGIN.left"
              :x2="CHART_WIDTH - MARGIN.right"
              :y1="tick.y"
              :y2="tick.y"
              class="ui-chart-gridline"
              :class="{ 'ui-chart-gridline--base': index === yTicks.length - 1 }"
            />
          </g>

          <path v-if="areaPath !== null" :d="areaPath" :fill="`url(#${gradientId})`" class="ui-chart-area" />
          <path v-if="linePath !== null" :d="linePath" class="ui-chart-line" />
          <circle
            v-if="points.length === 1"
            :cx="points[0].x"
            :cy="points[0].y"
            r="4"
            class="ui-chart-marker"
          />

          <g v-if="hoveredPoint !== null">
            <line
              :x1="hoveredPoint.x"
              :x2="hoveredPoint.x"
              :y1="MARGIN.top"
              :y2="BASELINE_Y"
              class="ui-chart-crosshair"
            />
            <circle
              :cx="hoveredPoint.x"
              :cy="hoveredPoint.y"
              r="4"
              class="ui-chart-marker ui-chart-marker--active"
            />
            <g :transform="`translate(${readoutX}, ${readoutY})`">
              <rect :width="READOUT_WIDTH" :height="READOUT_HEIGHT" rx="6" class="ui-chart-readout-bg" />
              <text x="8" y="16" class="ui-chart-readout-value">
                {{ formatValueWithUnit(hoveredPoint.value) }}
              </text>
              <text x="8" y="30" class="ui-chart-readout-at">{{ formatAt(hoveredPoint.at) }}</text>
            </g>
          </g>
        </svg>

        <!-- The time labels, one row under the plot. Each is placed at its tick's percentage and
             shifted by its anchor, so the first starts at the plot's left edge and the last ends
             at its right edge instead of hanging out of the card. -->
        <div ref="xAxisRow" class="relative col-start-2 row-start-2 mt-1.5 h-4" aria-hidden="true">
          <span
            v-for="tick in xTicks"
            :key="`x-${tick.x}`"
            class="ui-chart-axis-label absolute top-0 text-xs whitespace-nowrap text-text-muted tabular-nums"
            :style="{ left: xPercent(tick.x), translate: `${ANCHOR_SHIFT[tick.anchor]} 0` }"
            >{{ tick.label }}</span
          >
        </div>
      </div>

      <!-- `sr-only` sits on a wrapping div, never on the table itself: a table ignores the
           1px width `sr-only` gives it and sizes to its content, and being absolutely
           positioned it escapes the content panel's `overflow-x-hidden`. With a long caption
           (the Armenian one is ~400px) that widened the whole document at 390px. A div does
           honour the 1px box and its `overflow: hidden`, so the table inside is clipped. -->
      <div class="sr-only">
        <table>
          <caption>
            {{
              tableCaption
            }}
          </caption>
          <thead>
            <tr>
              <th scope="col">{{ t('app.chart.columnTime') }}</th>
              <th scope="col">{{ t('app.chart.columnValue') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="point in sortedSeries" :key="point.at">
              <td>{{ formatAt(point.at) }}</td>
              <td>{{ formatValueWithUnit(point.value) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>

<style scoped>
/*
 * Every stroke is non-scaling: the viewBox is stretched to the card's width, and a scaled 2 px
 * line becomes a 1 px hairline on a phone. The area's fade runs from a tint of the accent to
 * nothing; its opacity is a second channel on a graphic that the line above already draws at full
 * strength, so it is never asked to carry contrast.
 */
.ui-chart-line {
  fill: none;
  stroke: var(--color-accent);
  stroke-width: 2.5;
  stroke-linejoin: round;
  stroke-linecap: round;
  vector-effect: non-scaling-stroke;
}

.ui-chart-area {
  stroke: none;
}

.ui-chart-fade-top {
  stop-color: var(--color-accent);
  stop-opacity: 0.28;
}

.ui-chart-fade-bottom {
  stop-color: var(--color-accent);
  stop-opacity: 0;
}

.ui-chart-gridline {
  stroke: var(--color-border-subtle);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}

.ui-chart-gridline--base {
  stroke: var(--color-border-strong);
}

.ui-chart-marker {
  fill: var(--color-surface-1);
  stroke: var(--color-accent);
  stroke-width: 2;
  vector-effect: non-scaling-stroke;
}

.ui-chart-marker--active {
  fill: var(--color-accent);
}

.ui-chart-crosshair {
  stroke: var(--color-border-strong);
  stroke-width: 1;
  stroke-dasharray: 3 3;
  vector-effect: non-scaling-stroke;
}

.ui-chart-readout-bg {
  fill: var(--color-surface-2);
  stroke: var(--color-border-strong);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}

.ui-chart-readout-value {
  fill: var(--color-text-primary);
  font-family: var(--font-sans);
  font-size: var(--text-sm);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.ui-chart-readout-at {
  fill: var(--color-text-muted);
  font-family: var(--font-sans);
  font-size: var(--text-xs);
}
</style>
