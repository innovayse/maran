<script setup lang="ts">
/**
 * One entry in a {@link UiNav} list. It carries no styling of its own — the
 * link inside does that — and exists only so the list item stays a real `<li>`
 * without feature code writing the element (rules/vue.md).
 *
 * A `<nav>` whose children are not list items still reads as navigation, but a
 * screen reader then cannot announce "1 of 7", which is how a user knows how
 * much menu is left. Keeping the list real is what preserves that.
 */
</script>

<template>
  <li>
    <slot />
  </li>
</template>
