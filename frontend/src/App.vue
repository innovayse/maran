<script setup lang="ts">
/**
 * Root SPA shell: just the router-outlet. Holds no state, no header, and no
 * `<main>` of its own — every route nests under a layout
 * (`src/layouts/DefaultLayout.vue` or `AuthLayout.vue`), and rules/vue.md
 * requires exactly one `<main>` landmark per document, so this component
 * must not add a second one.
 */
</script>

<template>
  <RouterView />
</template>
