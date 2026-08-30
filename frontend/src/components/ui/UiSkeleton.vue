<script setup lang="ts">
/**
 * Loading placeholder standing in for content that has not arrived yet.
 * Purely decorative: it is hidden from assistive technology, because the
 * screen announces its loading state once via `UiSpinner`'s `role="status"`
 * — a dozen announcing placeholders would flood a screen reader instead of
 * informing it.
 *
 * The design does not pulse its placeholders, it sweeps them: the `--sk`
 * gradient is painted 800px wide and slid across the shape on a 1.3s linear
 * loop. That is why the animation lives in a `<style>` block here rather than
 * in a utility class — a keyframe set is the one thing Tailwind's utilities
 * cannot express, and the token file is not this component's to extend.
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
 * Tailwind utility classes describing the placeholder's shape and height. The
 * line heights are the design's own — 11px for a body line, with a step either
 * side of it — and the 5px radius is what its skeleton rows use throughout.
 */
const shapeClasses: ComputedRef<string> = computed(() => {
  const heights: Record<'sm' | 'md' | 'lg', string> = {
    sm: 'h-2.5',
    md: 'h-3',
    lg: 'h-3.5',
  }
  switch (props.shape) {
    case 'block':
      return props.size === 'lg' ? 'h-32 w-full rounded-xl' : 'h-20 w-full rounded-xl'
    case 'circle':
      return props.size === 'lg' ? 'size-11 rounded-full' : 'size-7 rounded-full'
    case 'line':
    default:
      return `${heights[props.size]} w-full rounded-md`
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

/* Halted for a reader who asked for less motion; the placeholder still reads as
   "not content yet" from its flat wash alone. */
@media (prefers-reduced-motion: reduce) {
  .ui-skeleton {
    animation: none;
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
