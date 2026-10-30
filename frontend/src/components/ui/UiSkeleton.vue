<script setup lang="ts">
/**
 * Loading placeholder standing in for content that has not arrived yet.
 * Purely decorative: it is hidden from assistive technology, because the
 * screen announces its loading state once via `UiSpinner`'s `role="status"`
 * — a dozen announcing placeholders would flood a screen reader instead of
 * informing it.
 *
 * It sweeps rather than pulses: the `--sk` gradient (a token, so it follows
 * the theme) is painted 800px wide and slid across the shape on a 1.3s linear
 * loop. The animation lives in a `<style>` block rather than a utility class
 * because a keyframe set is the one thing Tailwind's utilities cannot
 * express, and the token file is not this component's to extend.
 *
 * Under `prefers-reduced-motion` the sweep stops and the placeholder holds a
 * flat wash of the pressed surface instead of freezing mid-gradient, where it
 * would look like a stripe of half-painted content. It still reads as "not
 * content yet" from the flat shape alone.
 *
 * It carries no information and is hidden from assistive technology, so the
 * 3:1 bar for meaningful graphics does not apply; it is kept faint on purpose,
 * because a placeholder that stood out against its panel would pull the eye
 * away from the content about to replace it.
 *
 * **Composite shapes.** `table` stands in for a whole `UiTable`: a short
 * header line over `rows` rows of three cells, separated by the same hairline
 * the real rows carry. `card` stands in for a `UiCard`: a title line, two body
 * lines and a footer stub inside a `rounded-panel` frame. Both are composed of
 * the same shimmering bars, so they sweep in step.
 */
import { computed, type ComputedRef } from 'vue'

/** Props accepted by {@link UiSkeleton}. */
const props = withDefaults(
  defineProps<{
    /**
     * Shape of the placeholder: a single text line, a block, a circle (avatar/icon slot), or a
     * composite standing in for a whole table or card.
     */
    shape?: 'line' | 'block' | 'circle' | 'table' | 'card'
    /** Number of body rows in the `table` shape; ignored by the others. */
    rows?: number
    /** Height utility class token expressed as a size step, matching the kit's small/medium/large scale. */
    size?: 'sm' | 'md' | 'lg'
  }>(),
  { shape: 'line', size: 'md', rows: 5 },
)

/**
 * Tailwind utility classes describing the placeholder's shape and height. A
 * line is a pill, a block takes the panel radius it stands in for, and a
 * circle is a circle.
 */
const shapeClasses: ComputedRef<string> = computed(() => {
  const heights: Record<'sm' | 'md' | 'lg', string> = {
    sm: 'h-2',
    md: 'h-2.5',
    lg: 'h-3',
  }
  switch (props.shape) {
    case 'block':
      return props.size === 'lg' ? 'h-24 w-full rounded-panel' : 'h-16 w-full rounded-panel'
    case 'circle':
      return props.size === 'lg' ? 'size-9 rounded-full' : 'size-6 rounded-full'
    case 'line':
    default:
      return `${heights[props.size]} w-full rounded-full`
  }
})
</script>

<template>
  <div v-if="shape === 'table'" class="w-full" aria-hidden="true">
    <div class="flex gap-4 border-b border-border-subtle px-3 pb-2">
      <span class="ui-skeleton block h-2 w-1/4 rounded-full" />
      <span class="ui-skeleton block h-2 w-1/5 rounded-full" />
      <span class="ui-skeleton ml-auto block h-2 w-12 rounded-full" />
    </div>
    <div
      v-for="row in rows"
      :key="row"
      class="flex items-center gap-4 border-b border-border-subtle px-3 py-3 last:border-b-0"
    >
      <span class="ui-skeleton block h-2.5 w-1/3 rounded-full" />
      <span class="ui-skeleton block h-2.5 w-1/4 rounded-full" />
      <span class="ui-skeleton ml-auto block size-6 rounded-inset" />
    </div>
  </div>
  <div
    v-else-if="shape === 'card'"
    class="flex w-full flex-col gap-3 rounded-panel border border-border-subtle bg-surface-1 p-4"
    aria-hidden="true"
  >
    <span class="ui-skeleton block h-3 w-2/5 rounded-full" />
    <span class="ui-skeleton block h-2.5 w-full rounded-full" />
    <span class="ui-skeleton block h-2.5 w-4/5 rounded-full" />
    <span class="ui-skeleton mt-2 block h-6 w-24 rounded-control" />
  </div>
  <span v-else class="ui-skeleton block" :class="shapeClasses" aria-hidden="true" />
</template>

<style scoped>
.ui-skeleton {
  background: var(--sk);
  background-size: 800px 100%;
  animation: ui-skeleton-shimmer 1.3s linear infinite;
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
    background-position: -420px 0;
  }
  100% {
    background-position: 420px 0;
  }
}
</style>
