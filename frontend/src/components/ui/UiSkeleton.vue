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
 */
import { computed, type ComputedRef } from 'vue'

/** Props accepted by {@link UiSkeleton}. */
const props = withDefaults(
  defineProps<{
    /** Shape of the placeholder: a single text line, a stack of lines, or a circle (avatar/icon slot). */
    shape?: 'line' | 'block' | 'circle'
    /** Height utility class token expressed as a size step, matching the kit's small/medium/large scale. */
    size?: 'sm' | 'md' | 'lg'
  }>(),
  { shape: 'line', size: 'md' },
)

/**
 * Tailwind utility classes describing the placeholder's shape and height. A
 * line is a pill, a block takes the panel radius it stands in for, and a
 * circle is a circle.
 */
const shapeClasses: ComputedRef<string> = computed(() => {
  const heights: Record<'sm' | 'md' | 'lg', string> = {
    sm: 'h-2.5',
    md: 'h-3',
    lg: 'h-3.5',
  }
  switch (props.shape) {
    case 'block':
      return props.size === 'lg' ? 'h-32 w-full rounded-panel' : 'h-20 w-full rounded-panel'
    case 'circle':
      return props.size === 'lg' ? 'size-11 rounded-full' : 'size-7 rounded-full'
    case 'line':
    default:
      return `${heights[props.size]} w-full rounded-full`
  }
})
</script>

<template>
  <span class="ui-skeleton block" :class="shapeClasses" aria-hidden="true" />
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
