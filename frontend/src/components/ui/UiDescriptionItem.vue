<script setup lang="ts">
/**
 * One label/value pair inside a {@link UiDescriptionList}.
 *
 * The term is a prop rather than a slot because it is always a single already translated string,
 * and a prop is what makes that obvious at the call site; the value is a slot, because it is as
 * often a badge or a formatted date as it is text.
 *
 * The wrapping `<div>` keeps the `<dt>` and its `<dd>` in ONE grid cell. Without it the two become
 * separate grid children and a two-column list interleaves them, so a value ends up beside a label
 * it does not belong to. It also carries `min-w-0`: a grid child will not shrink below its content,
 * so one long unbroken value would otherwise push the whole panel into a horizontal scroll.
 *
 * The type sizes live here and nowhere else, which is the point: the label was `text-sm` on two
 * screens and `text-base` on a third for the same role, and nothing on either screen showed the
 * mismatch. The label is the design's small tag in sentence case (`text-xs`, display face,
 * `text-text-muted` — 5.36:1 on a panel, 5.03:1 on the raised surface, 4.62:1 on the third step in
 * dark; 5.41 / 4.91 / 4.57 in light). Labels wrap anywhere: a long Armenian or Russian
 * label would otherwise be wider than a phone-width column. The value sits one step above it in
 * `text-sm`, primary colour, and wraps anywhere too, for the same reason (a domain or a path has no
 * spaces to break at).
 *
 * A `figure` value is a count read at a glance (a census's buckets): it is set large, in the display
 * face, with tabular digits, so the sizes stay here rather than on a span at the call site.
 *
 * While the {@link UiDescriptionList} around it is `loading`, the term stays (it is the screen's
 * real text) and the value is a {@link UiSkeleton} line in a box one value-line tall (a body line,
 * or a figure line for a `figure`), so the loaded value lands in the same space.
 */
import { inject, ref } from 'vue'
import { DESCRIPTION_LIST_LOADING_KEY } from '../../utils/descriptionListLoadingKey'
import UiSkeleton from './UiSkeleton.vue'

/** What this pair holds. */
const props = withDefaults(
  defineProps<{
    /** The label, already translated by the caller — the kit holds no copy. */
    term: string
    /**
     * Whether the value is machine text (a domain, a path, a version, an identifier) and must be
     * set in the monospace face, where a zero is distinguishable from an O and a column of values
     * lines up.
     */
    mono?: boolean
    /** Whether the value is a figure read at a glance, set large in the display face. */
    figure?: boolean
  }>(),
  { mono: false, figure: false },
)

/** Whether the enclosing list is still loading; an item outside a loading list never is. */
const loading = inject(DESCRIPTION_LIST_LOADING_KEY, ref(false))

/**
 * The typography classes for the value.
 * @returns The class string for this item's `<dd>`.
 */
const valueClass = (): string => {
  if (props.figure) {
    return 'font-display text-3xl font-black tabular-nums'
  }
  return props.mono ? 'text-sm font-mono tabular-nums' : 'text-sm'
}
</script>

<template>
  <div class="min-w-0">
    <dt
      class="font-display text-xs font-extrabold text-text-muted wrap-anywhere"
    >
      {{ term }}
    </dt>
    <dd class="mt-0.5 text-text-primary wrap-anywhere" :class="valueClass()">
      <!-- The line sits in a box one value line tall (`h-5`, the `text-sm` line height; `h-9`, the
           `text-3xl` one, for a figure), so the row already has the height the value will have. -->
      <span v-if="loading && figure" class="flex h-9 w-16 items-center"><UiSkeleton shape="line" size="lg" /></span>
      <span v-else-if="loading" class="flex h-5 w-2/3 items-center"><UiSkeleton shape="line" /></span>
      <slot v-else />
    </dd>
  </div>
</template>
