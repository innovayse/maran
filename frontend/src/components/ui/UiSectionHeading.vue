<script setup lang="ts">
/**
 * The `<h2>` that opens a section of a page — a card's title, or a band of a
 * long page — with the muted sentence that sometimes follows it, and an
 * optional link or action on the right ("Accounts · View all").
 *
 * The type and the internal spacing live here so there is one answer: nine
 * places once wrote this by hand and no two agreed — four used `mb-3`, three
 * `mb-2.5`, two no margin, and the gap to the sentence was 10px on one screen
 * and 4px on another. None of it was a decision anybody made.
 *
 * The block carries NO outer margin of its own, matching
 * {@link ./UiPageHeading.vue}: the caller passes `class="mb-3"`, or nothing
 * inside a flex column that already spaces its children. Spacing a component
 * bakes in is spacing its call sites have to undo.
 *
 * It is small uppercase display type, so it wraps anywhere: an uppercase
 * Armenian section title is one long word and must not push the card wider
 * than a 390px screen. The title column shrinks (`min-w-0`) and the action
 * keeps its size, so a long title wraps instead of squeezing the link.
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
    title: string;
    /**
     * The sentence under the title, explaining what the section is for. Muted
     * and sentence-case, because it is read once: a section is already
     * introduced by the page's own subtitle, and a second full-weight
     * paragraph here competes with the content it introduces.
     */
    subtitle?: string;
  }>(),
  { subtitle: undefined },
);

/**
 * Slots exposed by {@link UiSectionHeading}.
 * @property action Optional link or control on the right of the title, such as "View all". The caller supplies a kit link or button; it is not drawn when empty.
 */
defineSlots<{
  action?: () => unknown;
}>();
</script>

<template>
  <div class="flex items-start justify-between gap-3">
    <div class="min-w-0">
      <h2 class="font-display text-sm font-extrabold tracking-caps text-text-primary uppercase wrap-anywhere">
        {{ title }}
      </h2>
      <p v-if="subtitle !== undefined" class="mt-1 text-sm text-text-muted wrap-anywhere">
        {{ subtitle }}
      </p>
    </div>
    <div v-if="$slots.action" class="shrink-0 text-sm font-semibold">
      <slot name="action" />
    </div>
  </div>
</template>
