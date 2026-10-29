<script setup lang="ts">
/**
 * The product's own mark, as drawn in the app bar, the drawer and the sign-in screen.
 *
 * **Why this is not a `UiIcon`.** Every other glyph in this panel comes from
 * `lucide-vue-next` through that one wrapper, and rules/vue.md says so. A brand
 * mark is the one thing that cannot: it belongs to no icon set, it carries its
 * own colours rather than `currentColor`, and it must look identical here, in
 * the browser tab and on a phone's home screen. So it is served from the same
 * `/favicon.svg` those other two use, which is what makes them the same object
 * instead of three drawings that happen to resemble each other.
 *
 * **The mark is not a locale string.** A letter taken from the locale files would
 * change shape with the interface language (a translator writing a Cyrillic `М`),
 * so the mark is an image and only the product name is a locale key, because that
 * is where this codebase keeps it.
 *
 * **No plate behind it — and none may be added.** The mark is a keystone cut
 * from one ramp of blue; a filled square behind it would put one blue on
 * another and lose the stone's edges (the approved mockup says the same: "the
 * keystone carries its own blues"). The mark sits straight on the panel. This
 * component draws no background, border or padding for that reason, so a caller
 * cannot get a plate by accident either.
 *
 * Always decorative, like `UiIcon`, and for the same reason: every caller
 * draws the product name beside it, and a mark carrying its own accessible
 * name there would make the heading say "Maran" twice.
 */

withDefaults(
  defineProps<{
    /** Edge length in pixels. The app bar and the drawer use 30; the auth layout uses 40. */
    size?: number
  }>(),
  { size: 30 },
)
</script>

<template>
  <img
    src="/favicon.svg"
    class="block shrink-0 select-none"
    :width="size"
    :height="size"
    alt=""
    aria-hidden="true"
    decoding="async"
  />
</template>
