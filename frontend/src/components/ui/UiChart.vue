<script setup lang="ts">
/**
 * `loading`: while the series is still on its way the chart draws its real label over a
 * placeholder plot (a `block` of the panel's large step), never its empty state: "no readings yet"
 * is an answer, and a series not yet read has given none.
 *
 * Line-and-area chart for a time series — the panel's monitoring screens plot CPU, memory, disk and
 * network rate buckets through this one primitive. It composes four parts, each in its own file:
 * {@link UiChartPlot} (the SVG: gridlines, line, area, hover readout), {@link UiChartYAxis} and
 * {@link UiChartXAxis} (the value and time labels) and {@link UiChartReadings} (the data as a
 * table). This file owns the props, the scale and the number formatting they share.
 *
 * Behaviour: a filled area under a line, axis ticks on both edges, and a hover readout showing the
 * pointed-at bucket's value, formatted through the caller's `formatValue` when given and through a
 * plain one-decimal rounding otherwise. An empty `series` — the ordinary state before the first
 * bucket arrives, not an error — renders {@link UiEmptyState} instead of an SVG with nothing to
 * plot. Both that case, a single-point series and a perfectly flat one are handled by construction
 * rather than by catching a `NaN`: {@link computeValueRange} always returns a non-zero span, so no
 * divide-by-zero can reach a path's `d` attribute.
 *
 * The range is then widened to round steps ({@link niceChartRange}), so the three gridlines fall on
 * round numbers (`100 / 50 / 0`) rather than on the series' own extremes.
 *
 * **The axis labels are HTML beside the SVG, not `<text>` inside it.** The SVG is stretched to the
 * card's width, and text drawn in its coordinate space shrinks with it: at 390 px the ticks came
 * out near 6 px. The labels are therefore ordinary `text-xs` elements that keep their size at any
 * width, positioned by PERCENTAGES of the plotted box so they still land on their gridlines. Both
 * label groups are `aria-hidden`, like the SVG: the readings table carries the data. Colours: the
 * labels are `text-text-muted` (5.36:1 on a panel, 5.03:1 on the raised surface the readout sits on,
 * in dark; 5.41 / 4.91 in light).
 *
 * **The header reading is load-bearing.** The latest value's `<span>` carries `font-mono` on
 * purpose: `e2e/monitoring/monitoring.spec.ts` locates the reading as
 * `.ui-chart span.font-mono` and asserts its exact text (e.g. "45.5 %"), so the class is the
 * spec's contract, not only a style. It also suits the content — a figure read against others
 * wants fixed-width digits, as in the kit's other numeric readings (`UiMeter`, `UiPageHeading`).
 * Keep it the ONLY `font-mono` span in the chart's root, and keep its text to the formatted value
 * with its unit and nothing else.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AppLocale } from '../../types/app'
import {
  computeChartPoints,
  computeValueRange,
  computeYTicks,
  sortChartSeries,
  type ChartPlottedPoint,
  type ChartValueRange,
} from '../../utils/chartGeometry'
import type { UiChartYTick } from '../../utils/chartView'
import { formatChartInstant } from '../../utils/formatChartInstant'
import { niceChartRange } from '../../utils/niceChartRange'
import UiChartPlot from './UiChartPlot.vue'
import UiChartReadings from './UiChartReadings.vue'
import UiChartXAxis from './UiChartXAxis.vue'
import UiChartYAxis from './UiChartYAxis.vue'
import UiEmptyState from './UiEmptyState.vue'
import UiIcon from './UiIcon.vue'
import UiSkeleton from './UiSkeleton.vue'

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
  /** Whether the series is still on its way; see the component note. */
  loading?: boolean
}>()

const { t, locale } = useI18n()

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
 * zero-width, and {@link niceChartRange} for why it is then widened to round values.
 */
const valueRange: ComputedRef<ChartValueRange> = computed(() => {
  return niceChartRange(
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

/**
 * Three horizontal gridlines/labels: the range's max, midpoint and min. {@link computeYTicks}
 * selects the values and their pixel rows; the label text is formatted here, since it depends on
 * the caller's own `formatValue` prop, which the geometry module deliberately knows nothing about.
 */
const yTicks: ComputedRef<UiChartYTick[]> = computed(() => {
  return computeYTicks(valueRange.value).map((tick) => {
    return { y: tick.y, label: formatValueLabel(tick.value) }
  })
})

/** The pixel rows of the gridlines, for the plot to draw. */
const gridRows: ComputedRef<number[]> = computed(() => {
  return yTicks.value.map((tick) => {
    return tick.y
  })
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
</script>

<template>
  <div class="ui-chart">
    <template v-if="loading">
      <div class="mb-2">
        <span class="font-display text-xs font-extrabold text-text-secondary wrap-anywhere">{{ label }}</span>
      </div>
      <UiSkeleton shape="block" size="lg" />
    </template>

    <UiEmptyState
      v-else-if="points.length === 0"
      :title="label"
      :description="t('app.chart.emptyDescription')"
    >
      <template #icon>
        <UiIcon name="pulse" />
      </template>
    </UiEmptyState>

    <template v-else>
      <div class="mb-2 flex items-baseline justify-between gap-3">
        <span
          class="font-display text-xs font-extrabold text-text-secondary wrap-anywhere"
          >{{ label }}</span
        >
        <span
          v-if="latestFormatted !== null"
          class="font-mono text-base font-semibold text-text-primary tabular-nums wrap-anywhere"
          >{{ latestFormatted }}</span
        >
      </div>

      <div class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-2">
        <UiChartYAxis :ticks="yTicks" />
        <UiChartPlot
          :points="points"
          :grid-rows="gridRows"
          :format-reading="formatValueWithUnit"
          :format-instant="formatAt"
        />
        <UiChartXAxis :points="points" :format-instant="formatAt" />
      </div>

      <UiChartReadings
        :series="sortedSeries"
        :caption="tableCaption"
        :format-instant="formatAt"
        :format-reading="formatValueWithUnit"
      />
    </template>
  </div>
</template>
