<script setup lang="ts">
/**
 * The product's own mark, as drawn in the shell and on the sign-in screen.
 *
 * **Why this is not a `UiIcon`.** Every other glyph in this panel comes from
 * `lucide-vue-next` through that one wrapper, and rules/vue.md says so. A brand
 * mark is the one thing that cannot: it belongs to no icon set, it carries its
 * own colours rather than `currentColor`, and it must look identical here, in
 * the browser tab and on a phone's home screen. So it is served from the same
 * `/favicon.svg` those other two use, which is what makes them the same object
 * instead of three drawings that happen to resemble each other.
 *
 * **What it replaced, and why that was wrong.** The shell drew the letter `M`
 * in an accent square — a placeholder from before the mark existed, still in
 * place long after it did, while the browser tab beside it already showed the
 * real thing. Worse, that letter came from the locale files as
 * `app.brandInitial`, so a translator doing exactly what those files invite
 * would have written a Cyrillic `М`, and the panel's mark would have changed
 * shape with the interface language. The key is gone; the product name stays a
 * locale key, because that is where this codebase keeps it.
 *
 * **No accent plate behind it.** The mark carries its own blues; a filled
 * square behind it would put one blue on another and lose the stone's edges.
 * The mark is the thing, not decoration inside a tile.
 *
 * Always decorative, like `UiIcon`, and for the same reason: every caller
 * draws the product name beside it — the collapsed rail draws it too, as
 * screen-reader-only text inside its heading. A mark carrying its own
 * accessible name there would make the heading say "Maran" twice.
 */

withDefaults(
  defineProps<{
    /** Edge length in pixels. The shell uses 28; the sign-in screen's wide panel, 40. */
    size?: number
  }>(),
  { size: 28 },
)
</script>

<template>
  <img
    src="/favicon.svg"
    class="shrink-0 select-none"
    :width="size"
    :height="size"
    alt=""
    aria-hidden="true"
    decoding="async"
  />
</template>
