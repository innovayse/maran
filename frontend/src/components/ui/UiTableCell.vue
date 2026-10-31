<script setup lang="ts">
/**
 * One data cell inside a {@link UiTable} body row. Carries the table's cell padding so every
 * screen's rows line up without each one restating the spacing (rules/vue.md: no raw elements, no
 * ad-hoc utility classes in feature code).
 *
 * The padding is dense (6px vertical, 12px horizontal) so a list shows more records per screen;
 * the row's height is still set by its tallest content (a name over an address, a badge, a menu
 * trigger), so a touch target in the row keeps its own size. Text is `text-sm`, the panel's body
 * step.
 *
 * `data-align` is exposed so {@link UiTable} can keep an end-aligned LAST cell (the row's actions)
 * sticky at the right edge without the caller passing anything else, and `data-placement` names
 * the part of a card the cell is, which the phone-width specs measure.
 *
 * **A cell has a floor width (`min-w-20`).** A table wider than its wrapper scrolls inside it
 * ({@link UiTable}); it must never squash instead. A caller that lets long text break anywhere
 * (`wrap-anywhere`, `break-all`) lowers the cell's minimum content width to a single character, and
 * the browser then happily shrinks that column until the text runs one letter per line. The floor
 * keeps every column readable, and the table grows past its wrapper instead.
 *
 * **On a phone the cell is a line of a card** ({@link UiTable}'s card layout, issue #217). Its
 * `placement` then says which part of the card it is: the `title` (the row's identity, top left),
 * the `actions` (top right, always in view), or a `line`, which draws the column's `label` before
 * its value so a reader who cannot see the header row still knows what each value is. The label is
 * `aria-hidden`: the table keeps its roles, so assistive technology already announces the column
 * header with the cell, and reading the word twice helps nobody. In the card the floor width and
 * the one-line rule are off, since there is no column to widen and a long value must wrap inside the
 * card rather than push it past the screen.
 */
import { computed, type ComputedRef } from 'vue'
import type { CellAlign, ColumnMinWidth } from '../../types/tableColumn'

/**
 * Which text tier the cell is set in. `primary` is what a row is scanned for (a name), `secondary`
 * is supporting detail (a domain, a type), `muted` is context that is never the thing being
 * looked for (a timestamp).
 */
export type CellTone = 'primary' | 'secondary' | 'muted'

/**
 * Where the cell sits: `column` is a cell of the ordinary table; the other three are the parts of a
 * row drawn as a card — its `title`, its `actions` and a labelled `line`.
 */
export type CellPlacement = 'column' | 'title' | 'actions' | 'line'

/** Props accepted by {@link UiTableCell}. */
const props = withDefaults(
  defineProps<{
    /**
     * Where the content sits in the column. Pass the same value to this column's
     * {@link UiTableHeaderCell}: a right-aligned row action under a left-aligned heading names a
     * column the heading does not sit above, and on a wide table the two are inches apart.
     */
    align?: CellAlign
    /**
     * Whether the content is a figure (a count, a size, a port, an identifier) and must be set in
     * the monospace face with tabular digits, so a column of numbers lines up on the digit and a
     * zero is distinguishable from an O. Right-aligning a numeric column is the caller's call,
     * through `align`, because a mono identifier is just as often left-aligned.
     */
    mono?: boolean
    /**
     * The text tier. It is a prop, not a utility class on the caller's element, because a colour
     * baked into the cell's template would fight a caller's `text-text-muted` depending on
     * stylesheet order, and every "muted" date would render primary. With no colour fixed in the
     * template the tier is decided in exactly one place.
     */
    tone?: CellTone
    /**
     * Keeps the content on one line (a name, an address, a date, a badge), so the column widens and
     * the table scrolls inside its container instead of breaking a value mid-way. A prop rather than
     * a class on the tag, for the same reason as `tone`.
     */
    nowrap?: boolean
    /**
     * Sets the content in the medium weight: what a row is scanned for (a name, an address). A prop
     * rather than a class on the content, so the weight is decided where the tone is.
     */
    strong?: boolean
    /** The column's least width in the table layout; the cell's own floor when left out. */
    minWidth?: ColumnMinWidth
    /** Which part of the row the cell is; see {@link CellPlacement}. */
    placement?: CellPlacement
    /** The column's heading, drawn before the value of a card `line`; ignored elsewhere. */
    label?: string
    /** Whether the card's title takes the whole width, because the row has no actions column. */
    wide?: boolean
  }>(),
  {
    align: 'start',
    mono: false,
    tone: 'primary',
    nowrap: false,
    strong: false,
    minWidth: undefined,
    placement: 'column',
    label: '',
    wide: false,
  },
)

/** The class of each {@link ColumnMinWidth}, as whole literals so the stylesheet generator sees them. */
const MIN_WIDTH_CLASSES: Record<ColumnMinWidth, string> = {
  '10rem': 'min-w-40',
  '11rem': 'min-w-44',
  '12rem': 'min-w-48',
  '14rem': 'min-w-56',
}

/** The text colour class of each {@link CellTone}. */
const TONE_CLASSES: Record<CellTone, string> = {
  primary: 'text-text-primary',
  secondary: 'text-text-secondary',
  muted: 'text-text-muted',
}

/** The box of the cell in its placement: a table column, or one part of a card. */
const placementClasses: ComputedRef<string> = computed(() => {
  switch (props.placement) {
    case 'title':
      return `${props.wide ? 'col-span-2' : 'col-start-1'} row-start-1 block min-w-0 self-center text-left wrap-anywhere`
    case 'actions':
      return 'col-start-2 row-start-1 block self-start text-right'
    case 'line':
      return 'col-span-2 flex min-w-0 items-baseline justify-between gap-3'
    default: {
      const alignment = props.align === 'end' ? 'text-right' : 'text-left'
      const wrapping = props.nowrap ? 'whitespace-nowrap' : ''
      const floor = props.minWidth === undefined ? 'min-w-20' : MIN_WIDTH_CLASSES[props.minWidth]
      return `${floor} px-3 py-1.5 align-middle ${alignment} ${wrapping}`
    }
  }
})

/**
 * The typography and alignment classes for this column.
 *
 * Right-aligning a row-action trigger with a flex wrapper inside the cell would leave the heading
 * at the far left of a column whose only content sits at the far right — visibly broken, and
 * unfixable from the heading because it cannot say where its column's content lives. Alignment is
 * therefore a prop on BOTH primitives.
 */
const cellClasses: ComputedRef<string> = computed(() => {
  const colour = TONE_CLASSES[props.tone]
  const face = props.mono ? 'font-mono tabular-nums' : ''
  const weight = props.strong ? 'font-medium' : ''
  return `${colour} ${face} ${weight} ${placementClasses.value}`
})
</script>

<template>
  <td :class="cellClasses" :data-align="align" :data-placement="placement">
    <template v-if="placement === 'line'">
      <span class="shrink-0 font-sans text-xs font-normal text-text-muted" aria-hidden="true">{{ label }}</span>
      <span class="min-w-0 text-right wrap-anywhere"><slot /></span>
    </template>
    <slot v-else />
  </td>
</template>
