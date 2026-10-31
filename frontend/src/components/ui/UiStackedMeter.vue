<script setup lang="ts">
/**
 * One horizontal bar split into several segments that together make a whole, with a legend that
 * prints every segment's name and figure as text. The disk card on the dashboard and on the
 * monitoring screen is its caller: a filesystem's size is used, reserved for root and available,
 * and those three always sum to the size (issue #77). {@link UiMeter} draws one part of an allowance;
 * this draws all the parts, which is why it is a second primitive rather than a prop on the first.
 *
 * **The legend is the meaning; the bar is a picture of it.** Each segment's caller-translated label
 * and caller-formatted figure are visible text in a list under the bar, so a reader who cannot tell
 * the colours apart — or a screen reader — loses nothing. The bar itself is `role="img"` with the
 * caller's translated `label` as its accessible name, a sentence summarising every segment, because
 * a group of coloured boxes has no name of its own to announce. The swatches in the legend are
 * `aria-hidden`: they repeat the colour, not the information.
 *
 * **A segment whose value is 0 still has its legend row**, with its figure ("0 B"), and draws no box
 * in the bar. A filesystem that keeps no reserve (xfs) is told as "reserved: 0 B", never by the row
 * silently missing, so the reader always sees every figure the caller passed.
 *
 * **Widths are proportions of the segments' sum**, laid out with `flex-grow`, so the boxes fill the
 * track exactly whatever the gaps between them. A sum of zero draws the empty track.
 *
 * **Colours, measured** with the WCAG relative-luminance formula. Each box is a graphic (3:1
 * needed) against the card it sits on (`--s1`) and against the track (`--s3`), dark / light:
 * `accent` 4.81 / 3.89 on the card, 4.16 / 3.29 on the track; `warning` 8.25 / 5.68 and
 * 7.12 / 4.79; `neutral` (the muted text tier, used as a fill) 5.36 / 5.41 and 4.62 / 4.57. Adjacent
 * boxes are NOT distinguishable from each other by colour alone (warning beside neutral is 1.05:1 in
 * light), which is why they are separated by a gap showing the card behind them and why the legend
 * text carries the meaning.
 */
import { computed, type ComputedRef } from 'vue'

/** The fill a segment is drawn in; see the measured contrast in the component note. */
export type UiStackedMeterTone = 'accent' | 'warning' | 'neutral'

/** One part of the whole, as the caller describes it. */
export interface UiStackedMeterSegment {
  /** Stable key for the list. */
  key: string
  /** The part's name, already translated by the caller. */
  label: string
  /** The part's size, in the same unit as every other segment's. */
  value: number
  /** The part's size as a reader sees it, already formatted by the caller. */
  valueText: string
  /** The fill the part is drawn in. */
  tone: UiStackedMeterTone
}

/** Props accepted by {@link UiStackedMeter}. */
const props = defineProps<{
  /** The parts, in the order they are drawn left to right and listed in the legend. */
  segments: UiStackedMeterSegment[]
  /** Accessible name for the bar: one translated sentence summarising every segment. */
  label: string
}>()

/** The fill class for each tone; tokens only. */
const TONE_CLASSES: Readonly<Record<UiStackedMeterTone, string>> = {
  accent: 'bg-accent',
  warning: 'bg-warning',
  neutral: 'bg-text-muted',
}

/** The segments that draw a box: those with something in them. Every segment keeps its legend row. */
const drawn: ComputedRef<UiStackedMeterSegment[]> = computed(() => {
  return props.segments.filter((segment) => {
    return segment.value > 0
  })
})

/**
 * The fill class for a segment's tone.
 * @param tone The segment's tone.
 * @returns The Tailwind background class backed by the tone's token.
 */
const toneClass = (tone: UiStackedMeterTone): string => {
  return TONE_CLASSES[tone]
}
</script>

<template>
  <div class="flex w-full flex-col gap-3">
    <div class="flex h-2 w-full gap-0.5 overflow-hidden rounded-full bg-surface-3" role="img" :aria-label="label">
      <div
        v-for="segment in drawn"
        :key="segment.key"
        class="h-full min-w-0.5 basis-0"
        :class="toneClass(segment.tone)"
        :style="{ flexGrow: segment.value }"
      />
    </div>
    <ul class="m-0 flex list-none flex-col gap-1.5 p-0">
      <li v-for="segment in segments" :key="segment.key" class="flex items-center gap-2 text-sm">
        <span class="size-2.5 shrink-0 rounded-full" :class="toneClass(segment.tone)" aria-hidden="true" />
        <span class="min-w-0 flex-1 break-words text-text-secondary">{{ segment.label }}</span>
        <span class="font-mono text-text-primary tabular-nums whitespace-nowrap">{{ segment.valueText }}</span>
      </li>
    </ul>
  </div>
</template>
