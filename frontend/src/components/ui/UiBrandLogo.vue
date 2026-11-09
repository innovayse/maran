<script setup lang="ts">
/**
 * A reseller's logo: the one component that draws an image the reseller supplied, in the shell's
 * wordmark, the branding screen's preview and the chosen-file thumbnail.
 *
 * The logo is untrusted data (the panel accepts a PNG, JPEG or WebP data URI and nothing else), so
 * it is only ever bound to `src` of an image, never to markup. It is decorative like
 * {@link UiBrandMark}: every caller prints the name beside it, so it carries `alt=""` and
 * `aria-hidden`. Its height is fixed by `size` and its width follows the image up to a ceiling, so a
 * wide logo cannot push the app bar's controls off the row.
 */

withDefaults(
  defineProps<{
    /** The logo as a data URI. */
    src: string
    /** `md` is the header height (32px, at most 8rem wide); `lg` the thumbnail beside the chooser (40px, 10rem). */
    size?: 'md' | 'lg'
  }>(),
  { size: 'md' },
)
</script>

<template>
  <img
    :src="src"
    class="block w-auto shrink-0 select-none object-contain"
    :class="size === 'lg' ? 'h-10 max-w-40' : 'h-8 max-w-32'"
    alt=""
    aria-hidden="true"
    decoding="async"
  />
</template>
