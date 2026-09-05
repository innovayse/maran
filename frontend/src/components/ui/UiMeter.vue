<script setup lang="ts">
/**
 * A horizontal ratio bar: how much of a bounded allowance is used. The monitoring screen's
 * per-account disk table is its first caller — used against quota — and any other bounded pair
 * (a plan's mailboxes, a licence's seats) reads the same way.
 *
 * It is a kit primitive rather than two `<div>`s written into the screen that needed it, for the
 * usual reason (rules/vue.md: "A missing primitive is not a licence to inline markup: add the
 * primitive to `components/ui/`, then use it") and for one specific to a bar: the accessible name
 * and the announced value are the whole of what a bar means to a screen reader, and a bar drawn
 * inline gets them wrong once per screen. Here they are `role="progressbar"` with its three ARIA
 * values plus a required, caller-translated `label` — required, not defaulted, because a default
 * is how a label silently stays English forever.
 *
 * **The bar never carries the meaning alone.** Callers render the figures beside it; colour is a
 * second channel, not the only one. The tone crosses to a warning past 80% and to danger at or past
 * 100% — the two moments an operator acts on — and those thresholds live here so every meter in
 * the panel agrees on where "nearly full" begins.
 *
 * `max` of zero is an ordinary input, not a guard against a caller's bug: a plan can record a zero
 * allowance, and what that means belongs to the module that owns the plan. The bar draws empty and
 * announces nothing beyond its label rather than dividing by it.
 */
import { computed, type ComputedRef } from 'vue'

/** Props accepted by {@link UiMeter}. */
const props = defineProps<{
  /** How much of the allowance is used, in the caller's own unit. */
  value: number
  /** The allowance, in the same unit as {@link value}. Zero means "no allowance recorded". */
  max: number
  /** Accessible name for the bar, already translated by the caller. */
  label: string
  /** The ratio spelled out for assistive technology, already translated and formatted. */
  valueText: string
}>()

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

/** The fill's colour: the accent until 80%, a warning past it, danger at or past the allowance. */
const fillClasses: ComputedRef<string> = computed(() => {
  if (ratio.value >= 1) {
    return 'bg-danger'
  }
  return ratio.value >= 0.8 ? 'bg-warning' : 'bg-accent'
})
</script>

<template>
  <div
    class="h-2 w-full overflow-hidden rounded-full bg-surface-3"
    role="progressbar"
    :aria-label="label"
    :aria-valuenow="value"
    :aria-valuemin="0"
    :aria-valuemax="max"
    :aria-valuetext="valueText"
  >
    <div class="h-full rounded-full transition-all" :class="fillClasses" :style="{ width: fillWidth }" />
  </div>
</template>
