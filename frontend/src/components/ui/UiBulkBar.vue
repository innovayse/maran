<script setup lang="ts">
/**
 * The bar that appears above a list once rows are selected: the selection in words ("3 selected")
 * and the actions that apply to all of them, which the caller puts in the default slot as
 * `UiButton`s.
 *
 * **Hidden at zero.** With nothing selected there is nothing to act on, so the bar is not rendered
 * at all. The polite live region is rendered ALWAYS, outside the bar: a region inserted together
 * with its text is not reliably announced, and a screen-reader user who ticks a row must hear the
 * new count without moving to find it. Clearing the selection empties the region quietly.
 *
 * **Look.** An accent-tinted strip: the `bg-accent-soft` wash with an accent hairline, on the
 * panel surface. The count is `text-accent-text`, 5.15:1 on that wash over a panel (the design
 * README's measured pair); it must not be placed over `bg-surface-3` (4.40:1). Actions wrap onto a
 * second line on a narrow screen instead of pushing the page sideways.
 */
import { computed, type ComputedRef } from 'vue'

/** Props accepted by {@link UiBulkBar}. */
const props = defineProps<{
  /** How many rows are selected; the bar is hidden at zero. */
  count: number
  /** The selection in words, already translated and pluralised ("3 selected"). */
  label: string
  /** Accessible name of the actions group, already translated ("Bulk actions"). */
  actionsLabel: string
}>()

/**
 * Slots exposed by {@link UiBulkBar}.
 * @property default The actions applied to the selected rows.
 */
defineSlots<{
  default?: () => unknown
}>()

/** Whether the bar is shown. */
const visible: ComputedRef<boolean> = computed(() => {
  return props.count > 0
})
</script>

<template>
  <div>
    <div
      v-if="visible"
      class="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-control border border-accent/40 bg-accent-soft px-3 py-2"
      data-testid="bulk-bar"
    >
      <span class="text-sm font-semibold text-accent-text" aria-hidden="true">{{ label }}</span>
      <div class="ml-auto flex flex-wrap items-center gap-2" role="group" :aria-label="actionsLabel">
        <slot />
      </div>
    </div>
    <span class="sr-only" aria-live="polite" aria-atomic="true" data-testid="bulk-bar-status">{{
      visible ? label : ''
    }}</span>
  </div>
</template>
