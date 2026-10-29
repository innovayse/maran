<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useId, useTemplateRef, type Ref } from 'vue'

/**
 * The panel's only table primitive. Renders a real `<table>` with a `<caption>` plus `<thead>` /
 * `<tbody>` structure; callers supply the header row through the `head` slot and the body rows
 * through the default slot, which keeps the primitive column-agnostic (rules/vue.md: UI comes from
 * `components/ui`).
 *
 * **The caption is visually hidden but never removed.** Most screens already show a heading above
 * their table, so a second visible title would only repeat it, yet the caption is what announces
 * the table's purpose to a screen reader that lands on it from a landmark list. `sr-only` keeps it
 * in the accessibility tree and out of the layout.
 *
 * **The table scrolls inside its own container, never the page.** A wide table (the backups list
 * has eight columns) is wider than a 390 px screen. If it were allowed to size the page, the whole
 * content panel would scroll sideways and drag the app bar's context with it. The wrapper is the
 * scroll container instead, so the overflow is contained and the page itself stays put. The wrapper
 * must stay the table's DIRECT parent: a row menu that is teleported to `body` is measured against
 * it, and the e2e specs for the backups and databases lists look it up as `table.parentElement`.
 *
 * **Columns keep a readable width; the table never squashes.** The overflow only happens if the
 * table is allowed to be wider than its wrapper, and that needs every column to have a minimum
 * width above one character. Headings and badges do not wrap and {@link UiTableCell} carries a
 * floor width, so a table with many columns grows past a narrow wrapper and scrolls inside it.
 *
 * **Flat on purpose.** The approved design draws a table as hairlines on the surface it sits on:
 * one rule under the header, one between rows, a tinted row on hover. There is no header band and
 * no zebra striping — a band is a second surface that has to be contrast-checked against every
 * header colour, and stripes fight the hover tint for the same job. The rules and the hover live on
 * {@link UiTableRow}; the header's type lives on {@link UiTableHeaderCell}.
 *
 * **An overflowing wrapper is a deliberate, labelled scroll region; a fitting one is nothing.**
 * Chrome makes a scroll container keyboard-focusable on its own, which left a wide table with an
 * unlabelled Tab stop (named after the whole table's text) drawn with the browser's thin default
 * outline instead of the design ring. The wrapper therefore says what it is: `role="region"` named
 * by the caption through `aria-labelledby`, and `tabindex="0"` so a keyboard user can scroll it with
 * the arrow keys. The stop exists ONLY while the content really overflows, measured with a
 * `ResizeObserver` on the wrapper and the table (a table can start or stop overflowing when its
 * rows, the language or the viewport change). A fitting table adds no Tab stop. The ring is the
 * design's `shadow-focus` (docs/design/redesign-2026-10/README.md: never rebuild it by hand) with
 * the outline removed; a shadow is drawn outside the wrapper's own box, which that box's `overflow`
 * does not clip, and the main region's padding leaves room for it.
 */

/** Props accepted by {@link UiTable}. */
defineProps<{
  /** Accessible name for the table, already translated by the caller. Visually hidden. */
  caption: string
}>()

/** Unique id tying the scroll region's accessible name to the caption. */
const captionId: string = useId()

/** The scroll wrapper, measured to decide whether it is a keyboard stop. */
const scroller = useTemplateRef<HTMLElement>('scroller')

/** The table inside it, observed so growth of the content (not only the viewport) is noticed. */
const tableElement = useTemplateRef<HTMLElement>('table')

/** Whether the content is wider than the wrapper right now. */
const overflows: Ref<boolean> = ref(false)

/** Observer that re-measures on any size change of the wrapper or the table. */
let observer: ResizeObserver | null = null

/**
 * Re-measures the wrapper. A one-pixel tolerance keeps sub-pixel rounding from turning a table
 * that exactly fits into a phantom Tab stop.
 * @returns Nothing; updates `overflows`.
 */
const measure = (): void => {
  const element = scroller.value
  overflows.value = element !== null && element.scrollWidth > element.clientWidth + 1
}

onMounted(() => {
  measure()
  observer = new ResizeObserver(measure)
  if (scroller.value !== null) {
    observer.observe(scroller.value)
  }
  if (tableElement.value !== null) {
    observer.observe(tableElement.value)
  }
})

onBeforeUnmount(() => {
  observer?.disconnect()
  observer = null
})
</script>

<template>
  <div
    ref="scroller"
    class="w-full max-w-full overflow-x-auto focus-visible:shadow-focus focus-visible:outline-none"
    :role="overflows ? 'region' : undefined"
    :aria-labelledby="overflows ? captionId : undefined"
    :tabindex="overflows ? 0 : undefined"
  >
    <table ref="table" class="w-full border-collapse text-left text-sm">
      <caption :id="captionId" class="sr-only">
        {{
          caption
        }}
      </caption>
      <thead>
        <slot name="head" />
      </thead>
      <tbody>
        <slot />
      </tbody>
    </table>
  </div>
</template>
