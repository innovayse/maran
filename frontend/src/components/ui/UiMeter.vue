<script setup lang="ts">
/**
 * A horizontal ratio bar: how much of a bounded allowance is used. The monitoring screen's
 * per-account disk table and the dashboard's resource card are its callers, and any other bounded
 * pair (a plan's mailboxes, a licence's seats) reads the same way.
 *
 * It is a kit primitive rather than two `<div>`s written into the screen that needed it
 * (rules/vue.md: "A missing primitive is not a licence to inline markup"), and for one reason
 * specific to a bar: the accessible name and the announced value are the whole of what a bar means
 * to a screen reader, and a bar drawn inline gets them wrong once per screen. Here they are
 * `role="progressbar"` with its three ARIA values plus a required, caller-translated `label` —
 * required, not defaulted, because a default is how a label silently stays English forever.
 *
 * **A bar must never reach the screen as an unlabelled stripe.** `label` and `valueText` reach
 * assistive technology through `aria-*`, but a bar that carried them only there would show a sighted
 * operator anonymous stripes whenever a card passed them and stopped. The figure is therefore drawn
 * as visible text BESIDE the bar by default — the ratio as a
 * whole percentage, and the caller's own `valueText` when there is no ratio to compute. A caller
 * that already prints its own figures next to the bar (the dashboard card prints the label and the
 * reading above it) turns this off with `:show-value="false"`; the default is the safe side, so
 * forgetting the prop yields a duplicated figure, never a missing one. The visible text is
 * `aria-hidden`: the progressbar already announces `valueText`, and reading both would say the
 * same thing twice.
 *
 * **The bar never carries the meaning alone.** Colour is a second channel on top of the text. The
 * tone crosses to a warning past 80 % and to danger at or past 100 % — the two moments an operator
 * acts on — and those thresholds live here so every meter in the panel agrees on where "nearly
 * full" begins. Because the percentage text is printed in the neutral tier, no status colour is
 * ever asked to pass as text.
 *
 * **Colours, measured.** The fill is a graphic (3:1 needed) on the third surface step, which is
 * the track: it stays distinguishable from the raised surface a hovered table row turns into, where
 * a raised-surface track would vanish. Dark / light: accent 4.16 / 3.29, warning 7.12 / 4.79,
 * danger 4.13 / 4.74. The percentage in `text-text-secondary` is 7.08 / 6.65 / 6.11 on the three
 * surfaces in dark and 6.00 / 5.44 / 5.06 in light.
 *
 * **Two tones, because two things are measured.** The default `capacity` tone is everything above:
 * "how full is this allowance", where a high reading is bad news. A TASK is the opposite — 100 %
 * is the good outcome — so painting a finished run red at the threshold that warns about a full
 * disk would be the meter lying. `tone="progress"` therefore ignores the ratio's thresholds and
 * colours the fill from the caller's `status` instead: accent while `running`, green for
 * `success`, red for `danger`. The status is an input rather than something derived from the
 * ratio because a failed task stops at 40 % and a finished one sits at 100 %; the ratio cannot
 * tell them apart, the task can. Callers that do not pass `tone` are unchanged.
 *
 * Progress-tone fills against the same track (graphic, 3:1 needed), dark / light: running
 * (accent) 4.16 / 3.29, success 6.15 / 4.58, danger 4.13 / 4.74. Measured with the WCAG relative
 * luminance formula on the third surface step; the status is still never the only carrier of
 * meaning, since callers print the figure and a status badge beside the bar.
 *
 * `max` of zero is an ordinary input, not a guard against a caller's bug: a plan can record a zero
 * allowance, and what that means belongs to the module that owns the plan. The bar draws empty,
 * shows the caller's `valueText` rather than a percentage of nothing, and never divides by it.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

/** What the meter measures: a bounded allowance, or the advance of a piece of work. */
export type UiMeterTone = 'capacity' | 'progress'

/** How a piece of work stands; only the `progress` tone reads it. */
export type UiMeterStatus = 'running' | 'success' | 'danger'

/** Props accepted by {@link UiMeter}. */
const props = withDefaults(
  defineProps<{
    /** How much of the allowance is used, in the caller's own unit. */
    value: number
    /** The allowance, in the same unit as {@link value}. Zero means "no allowance recorded". */
    max: number
    /** Accessible name for the bar, already translated by the caller. */
    label: string
    /** The ratio spelled out for assistive technology, already translated and formatted. */
    valueText: string
    /**
     * Whether to draw the figure as visible text beside the bar. On by default so a bar is never
     * left anonymous; pass `false` only where the surrounding markup already prints it.
     */
    showValue?: boolean
    /**
     * What the bar measures. `capacity` (the default) warns as the allowance fills; `progress`
     * colours by {@link status} instead, because for a task a full bar is the success case.
     */
    tone?: UiMeterTone
    /**
     * How the work stands, used only by the `progress` tone and ignored by `capacity`. Defaults to
     * `running`, the accent.
     */
    status?: UiMeterStatus
  }>(),
  { showValue: true, tone: 'capacity', status: 'running' },
)

const { locale } = useI18n()

/** The ratio the fill is drawn at: 0 when nothing bounds it, and never negative. */
const ratio: ComputedRef<number> = computed(() => {
  if (props.max <= 0) {
    return 0
  }
  return Math.max(props.value / props.max, 0)
})

/**
 * The fill's width as a percentage string, clamped at 100 so an over-quota account draws a full
 * bar rather than one that runs out of its own track.
 */
const fillWidth: ComputedRef<string> = computed(() => {
  return `${Math.min(ratio.value, 1) * 100}%`
})

/**
 * The fill's colour. Capacity: the accent until 80 %, a warning past it, danger at or past the
 * allowance. Progress: the caller's status, never the ratio, so a finished task stays green.
 */
const fillClasses: ComputedRef<string> = computed(() => {
  if (props.tone === 'progress') {
    switch (props.status) {
      case 'success':
        return 'bg-success'
      case 'danger':
        return 'bg-danger'
      default:
        return 'bg-accent'
    }
  }
  if (ratio.value >= 1) {
    return 'bg-danger'
  }
  return ratio.value >= 0.8 ? 'bg-warning' : 'bg-accent'
})

/**
 * The figure printed beside the bar. A real ratio prints as a whole percentage in the panel's
 * current language (not clamped: an account at 112 % of its quota says 112 %, which is the true and
 * the useful number); with no allowance there is no ratio, so the caller's own sentence is shown.
 */
const visibleText: ComputedRef<string> = computed(() => {
  if (props.max <= 0) {
    return props.valueText
  }
  return new Intl.NumberFormat(locale.value, { style: 'percent', maximumFractionDigits: 0 }).format(
    ratio.value,
  )
})
</script>

<template>
  <div class="flex w-full items-center gap-3">
    <div
      class="h-1.5 min-w-12 flex-1 overflow-hidden rounded-full bg-surface-3"
      role="progressbar"
      :aria-label="label"
      :aria-valuenow="value"
      :aria-valuemin="0"
      :aria-valuemax="max"
      :aria-valuetext="valueText"
    >
      <div
        class="h-full rounded-full transition-[width] duration-300 motion-reduce:transition-none"
        :class="fillClasses"
        :style="{ width: fillWidth }"
      />
    </div>
    <span
      v-if="showValue"
      class="font-mono text-xs text-text-secondary tabular-nums"
      aria-hidden="true"
      >{{ visibleText }}</span
    >
  </div>
</template>
