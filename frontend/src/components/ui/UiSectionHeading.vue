<script setup lang="ts">
/**
 * The `<h2>` that opens a section of a page — a card's title, or a band of a
 * long page — with the muted sentence that sometimes follows it.
 *
 * Nine places wrote this by hand and no two agreed on the spacing: four used
 * `mb-3`, three `mb-2.5`, two no margin at all, and the gap between the title
 * and its sentence was 10px on one screen and 4px on another. None of that was
 * a decision anybody made; it is the same failure the description list turned
 * up, where one screen's label sat a step off the same label on two others and
 * neither screen showed it. The type and the internal spacing live here now,
 * so there is one answer and a change to it is one edit.
 *
 * The block carries NO outer margin of its own, matching
 * {@link ./UiPageHeading.vue}: the caller passes `class="mb-3"`, or nothing
 * inside a flex column that already spaces its children. Spacing that a
 * component bakes in is spacing its call sites have to undo.
 *
 * This is `<h2>` and only `<h2>`. Every consumer sits directly under a page's
 * single `<h1>`, so there is no level to choose; a section that one day nests
 * inside another gets a level prop then, with a real second consumer to shape
 * it.
 */

/**
 * What the section heading shows.
 */
withDefaults(
  defineProps<{
    /** The section's title, already translated (or a name the server sent). */
    title: string
    /**
     * The sentence under the title, explaining what the section is for. Muted
     * and a step below the body face, because it is read once: a section is
     * already introduced by the page's own subtitle, and a second full-weight
     * paragraph here competes with the content it introduces.
     */
    subtitle?: string
  }>(),
  { subtitle: undefined },
)
</script>

<template>
  <div>
    <h2 class="text-lg font-semibold text-text-primary">{{ title }}</h2>
    <p v-if="subtitle !== undefined" class="mt-1 text-sm text-text-muted">{{ subtitle }}</p>
  </div>
</template>
