<script setup lang="ts">
/**
 * A status region only a screen reader perceives: it is in the page before it has anything to say, and what is put
 * in its default slot afterwards is spoken politely.
 *
 * It exists because a change of a control's visible name (a "Copy" button that becomes "Copied") is not announced
 * by itself. The feature component keeps its own state and fills the slot with the translated sentence, or leaves it
 * empty; the primitive owns no copy. `sr-only` hides it visually without taking it out of the accessibility tree.
 */
</script>

<template>
  <span class="sr-only" role="status"><slot /></span>
</template>
