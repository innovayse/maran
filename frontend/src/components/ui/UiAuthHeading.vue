<script setup lang="ts">
/**
 * The title block of a signed-out screen: the page's `h2` and an optional sentence under it. The
 * auth layout owns the brand and its wordmark is the screen's single `h1`, so each page's own title
 * sits one level below it and the outline reads "Maran › Sign in".
 *
 * It exists because six auth pages repeated the same title classes, and the copy had already drifted
 * once: `break-words` does not lower an element's min-content width, so a long Armenian
 * word kept the panel as wide as the word and overflowed it on a phone. `wrap-anywhere`
 * (`overflow-wrap: anywhere`) does count the break opportunities toward min-content, which is what
 * lets the panel shrink. The title is display type in sentence case; the
 * Russian and Armenian titles are longer than the English one and must wrap rather than widen the
 * panel. The heading level stays `h2` — specs and the page outline are written against it.
 *
 * The block carries no outer margin: a page that wants space below it passes a class (for example
 * `class="mb-6"`), which falls through to the root.
 */

/** Props accepted by {@link UiAuthHeading}. */
defineProps<{
  /** The screen's title, already translated. */
  title: string
  /** The sentence under the title. Omitted on a screen that needs none. */
  subtitle?: string
}>()
</script>

<template>
  <div class="min-w-0">
    <h2
      class="font-display text-2xl font-black text-text-primary wrap-anywhere"
    >
      {{ title }}
    </h2>
    <p v-if="subtitle !== undefined" class="mt-1.5 text-sm text-text-secondary wrap-anywhere">
      {{ subtitle }}
    </p>
  </div>
</template>
