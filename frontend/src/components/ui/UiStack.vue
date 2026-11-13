<script setup lang="ts">
/**
 * The one layout box of the panel: its children in a column or in a row with a named gap. A screen
 * that needs to space a few elements apart, or to line up a button with a link, uses this and never
 * a `<div class="flex ...">` of its own, because raw markup is allowed only inside `components/ui/`
 * (rules/vue.md) and a hand-written flex row is how two screens come to differ by a gap.
 *
 * It draws nothing of its own: no background, no padding, no border. It is not a landmark and has no
 * role, so it adds nothing for assistive technology; a group that needs a name is a `UiCard` or a
 * labelled region, not a stack.
 *
 * `min-w-0` is always set, so a stack inside a flex or grid cell can shrink below its content's width
 * and a long unbroken value (a machine id, a path) wraps or scrolls in its own box instead of pushing
 * the page sideways.
 *
 * Attributes written on the tag (`data-testid`, `class` for a margin) fall through to the box.
 */
import { computed, type ComputedRef } from 'vue'

/** Which way the children run. */
export type UiStackDirection = 'column' | 'row'

/** The named gap steps between the children: none, 4px, 8px, 12px and 16px. */
export type UiStackGap = 'none' | 'xs' | 'sm' | 'md' | 'lg'

/** How the children sit on the cross axis. `stretch` fills it, `start` keeps each child's own width, `center` centres them. */
export type UiStackAlign = 'stretch' | 'start' | 'center'

/** Where the children sit on the main axis. */
export type UiStackJustify = 'start' | 'end' | 'between'

/** The Tailwind class of each direction. Whole class names, so the build can see them. */
const DIRECTIONS: Record<UiStackDirection, string> = {
  column: 'flex-col',
  row: 'flex-row',
}

/** The Tailwind class of each gap step. */
const GAPS: Record<UiStackGap, string> = {
  none: 'gap-0',
  xs: 'gap-1',
  sm: 'gap-2',
  md: 'gap-3',
  lg: 'gap-4',
}

/** The Tailwind class of each cross-axis alignment. */
const ALIGNMENTS: Record<UiStackAlign, string> = {
  stretch: 'items-stretch',
  start: 'items-start',
  center: 'items-center',
}

/** The Tailwind class of each main-axis placement. */
const JUSTIFICATIONS: Record<UiStackJustify, string> = {
  start: 'justify-start',
  end: 'justify-end',
  between: 'justify-between',
}

/** Props accepted by {@link UiStack}. */
const props = withDefaults(
  defineProps<{
    /** Which way the children run. */
    direction?: UiStackDirection
    /** The space between the children. */
    gap?: UiStackGap
    /** Whether the children continue on a further line when the width runs out. */
    wrap?: boolean
    /** How the children sit on the cross axis. */
    align?: UiStackAlign
    /** Where the children sit on the main axis. */
    justify?: UiStackJustify
  }>(),
  { direction: 'column', gap: 'md', wrap: false, align: 'stretch', justify: 'start' },
)

/**
 * Slots exposed by {@link UiStack}.
 * @property default The children to lay out.
 */
defineSlots<{
  default?: () => unknown
}>()

/** The classes the box wears for the current props. */
const classes: ComputedRef<string[]> = computed(() => {return [
  'flex min-w-0',
  DIRECTIONS[props.direction],
  GAPS[props.gap],
  ALIGNMENTS[props.align],
  JUSTIFICATIONS[props.justify],
  props.wrap ? 'flex-wrap' : 'flex-nowrap',
]})
</script>

<template>
  <div :class="classes">
    <slot />
  </div>
</template>
