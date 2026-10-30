<script setup lang="ts">
/**
 * Loading placeholder standing in for content that has not arrived yet.
 * Purely decorative: every shape is `aria-hidden`, because the region around it announces its
 * loading state once ({@link UiLoadingRegion}'s polite status) — a dozen announcing placeholders
 * would flood a screen reader instead of informing it.
 *
 * **It never describes a screen on its own.** A table's placeholder rows are drawn by
 * {@link UiTable} from the same `columns` schema it renders (one `cell` per column, by `kind`); a
 * form's placeholders are the form's own fields, each keeping its real label and swapping only its
 * control for a `block` of that control's footprint. This component only knows shapes, so there is
 * no second description of any table or form to fall out of step with the real one.
 *
 * Shapes:
 * - `line`: a text line, full width; `size` picks the height (`sm`, `md`, `lg`).
 * - `circle`: an avatar or icon slot; `size` `sm` (16 px), `md` (24 px), `lg` (36 px).
 * - `block`: given `control`, exactly that control's footprint (`field`: an input or select at the
 *   control height; `area`: a textarea at its resting four rows; `switch`: the 36 by 20 px track;
 *   `checkbox`: the 16 px box). Without `control`, a panel-radius block (`size` `md` or `lg`).
 * - `cell`: one table cell's content, by the column's `kind` (see {@link UiTableColumnKind}).
 *   Inline, so the cell's own `text-align` places it exactly where the real content will sit.
 * - `kpi`: a figure tile's content: an icon square, a large figure line and a label line.
 * - `card`: a whole card: a title line, two body lines and a footer stub in a `rounded-panel` frame.
 *
 * Every size is a stock spacing step or a density token; nothing is a hand-typed length.
 *
 * **Visibility follows the region.** Inside a {@link UiLoadingRegion} the placeholder is mounted at
 * once (so the space is held) but stays `invisible` until the region's delayed flag turns on, so a
 * fast answer shows no flash. Outside a region it is visible.
 *
 * **The shimmer.** The `--sk` token is a gradient of the primary text colour at 6 %, 13 % and 6 %,
 * so it follows the theme. It is painted at twice the shape's width and slid across it on a 1.4 s
 * ease-in-out loop. The animation lives in a `<style>` block because a keyframe set is the one thing
 * Tailwind's utilities cannot express. Under `prefers-reduced-motion` the sweep stops
 * (`animation: none`) and the placeholder holds a flat wash of the pressed surface instead of
 * freezing mid-gradient, where it would look like a stripe of half-painted content.
 *
 * It carries no information and is hidden from assistive technology, so the 3:1 bar for meaningful
 * graphics does not apply; it is kept faint on purpose, because a placeholder that stood out
 * against its panel would pull the eye away from the content about to replace it.
 */
import { computed, inject, ref, type ComputedRef } from 'vue'
import { SKELETON_VISIBLE_KEY } from '../../utils/skeletonVisibleKey'
import type { UiTableColumnKind } from '../../types/tableColumn'

/** The shapes this placeholder can take. */
type SkeletonShape = 'line' | 'block' | 'circle' | 'cell' | 'kpi' | 'card'

/** A height step for `line`, `circle` and a control-less `block`. */
type SkeletonSize = 'sm' | 'md' | 'lg'

/** The form control a `block` stands in for, at that control's exact footprint. */
export type SkeletonControl = 'field' | 'area' | 'switch' | 'checkbox'

/** Props accepted by {@link UiSkeleton}. */
const props = withDefaults(
  defineProps<{
    /** The shape; see the component's doc comment. */
    shape?: SkeletonShape
    /** Height step of a `line`, `circle` or control-less `block`; ignored by the others. */
    size?: SkeletonSize
    /** The column kind a `cell` stands in for (`text` when left out); ignored by the other shapes. */
    kind?: UiTableColumnKind
    /** The form control a `block` stands in for; ignored by the other shapes. */
    control?: SkeletonControl
  }>(),
  { shape: 'line', size: 'md', kind: 'text', control: undefined },
)

/** The height class of each `line` step. */
const LINE_HEIGHTS: Record<SkeletonSize, string> = {
  sm: 'h-2',
  md: 'h-2.5',
  lg: 'h-3',
}

/** The box of each `circle` step. */
const CIRCLE_SIZES: Record<SkeletonSize, string> = {
  sm: 'size-4',
  md: 'size-6',
  lg: 'size-9',
}

/** The footprint of each form control, matching the control's own classes. */
const CONTROL_SHAPES: Record<SkeletonControl, string> = {
  field: 'h-control w-full rounded-control',
  area: 'h-24 w-full rounded-control',
  switch: 'h-5 w-9 shrink-0 rounded-full',
  checkbox: 'size-4 shrink-0 rounded-mark',
}

/**
 * The single-bar shape of each column kind (`identity` is composite and drawn in the template).
 * Widths are fixed stock steps so a column of placeholders reads as a column, not as noise.
 */
const CELL_SHAPES: Record<Exclude<UiTableColumnKind, 'identity'>, string> = {
  identifier: 'h-2.5 w-28 rounded-full',
  text: 'h-2.5 w-28 rounded-full',
  mono: 'h-2.5 w-24 rounded-full',
  code: 'h-2.5 w-40 rounded-full',
  badge: 'h-5 w-16 rounded-full',
  number: 'h-2.5 w-12 rounded-full',
  date: 'h-2.5 w-20 rounded-full',
  actions: 'size-8 rounded-control',
}

/** Whether the wait has lasted long enough to show placeholders; always, outside a region. */
const visible = inject(SKELETON_VISIBLE_KEY, ref(true))

/** Classes of a single-bar shape (`line`, `circle`, `block`); empty for the composite shapes. */
const barClasses: ComputedRef<string> = computed(() => {
  switch (props.shape) {
    case 'block':
      if (props.control !== undefined) {
        return CONTROL_SHAPES[props.control]
      }
      return props.size === 'lg' ? 'h-24 w-full rounded-panel' : 'h-16 w-full rounded-panel'
    case 'circle':
      return `${CIRCLE_SIZES[props.size]} shrink-0 rounded-full`
    case 'line':
      return `${LINE_HEIGHTS[props.size]} w-full rounded-full`
    default:
      return ''
  }
})

/** Classes of a non-identity `cell`. */
const cellClasses: ComputedRef<string> = computed(() => {
  return props.kind === 'identity' ? '' : CELL_SHAPES[props.kind]
})
</script>

<template>
  <span
    v-if="shape === 'cell' && kind === 'identity'"
    class="inline-flex items-center gap-2 align-middle"
    :class="visible ? '' : 'invisible'"
    data-skeleton="cell-identity"
    aria-hidden="true"
  >
    <span class="ui-skeleton block size-6 shrink-0 rounded-full" />
    <span class="ui-skeleton block h-2.5 w-32 rounded-full" />
  </span>
  <span
    v-else-if="shape === 'cell'"
    class="ui-skeleton inline-block align-middle"
    :class="[cellClasses, visible ? '' : 'invisible']"
    :data-skeleton="`cell-${kind}`"
    aria-hidden="true"
  />
  <span
    v-else-if="shape === 'kpi'"
    class="flex flex-col gap-2"
    :class="visible ? '' : 'invisible'"
    data-skeleton="kpi"
    aria-hidden="true"
  >
    <span class="ui-skeleton block size-9 rounded-control" />
    <span class="ui-skeleton block h-6 w-20 rounded-full" />
    <span class="ui-skeleton block h-2.5 w-24 rounded-full" />
  </span>
  <span
    v-else-if="shape === 'card'"
    class="flex w-full flex-col gap-3 rounded-panel border border-border-subtle bg-surface-1 p-4"
    :class="visible ? '' : 'invisible'"
    data-skeleton="card"
    aria-hidden="true"
  >
    <span class="ui-skeleton block h-3 w-2/5 rounded-full" />
    <span class="ui-skeleton block h-2.5 w-full rounded-full" />
    <span class="ui-skeleton block h-2.5 w-4/5 rounded-full" />
    <span class="ui-skeleton mt-2 block h-6 w-24 rounded-control" />
  </span>
  <span
    v-else
    class="ui-skeleton block"
    :class="[barClasses, visible ? '' : 'invisible']"
    :data-skeleton="shape"
    aria-hidden="true"
  />
</template>

<style scoped>
.ui-skeleton {
  background: var(--sk);
  background-size: 200% 100%;
  animation: ui-skeleton-shimmer 1.4s ease-in-out infinite;
}

/* Halted for a reader who asked for less motion, and flattened to one wash so
   the frozen frame is not a stripe of gradient. */
@media (prefers-reduced-motion: reduce) {
  .ui-skeleton {
    animation: none;
    background: var(--s3);
  }
}

@keyframes ui-skeleton-shimmer {
  0% {
    background-position: 100% 0;
  }
  100% {
    background-position: -100% 0;
  }
}
</style>
