<script setup lang="ts">
/**
 * Root SPA shell: just the router-outlet. Holds no state, no header, and no
 * `<main>` of its own — every route nests under a layout
 * (`src/layouts/DefaultLayout.vue` or `AuthLayout.vue`), and rules/vue.md
 * requires exactly one `<main>` landmark per document, so this component
 * must not add a second one.
 *
 * **A message belongs to the screen that showed it (issue #221).** The auth store holds one
 * failure message, read by every signed-out screen and by the sessions and two-factor settings
 * screens, so a refused invitation followed by "Back to sign in" greeted the visitor with the same
 * refusal on the sign-in screen. This is the one component that sees every move between screens,
 * in either layout, so it dismisses the message on each of them; no page clears it for itself.
 */
import { watch } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from './stores/auth'

const route = useRoute()
const authStore = useAuthStore()

// Keyed on the path: a query change on the same screen (a token in a link, a filter) is not a new
// screen, and its message still belongs to it.
watch(
  (): string => {
    return route.path
  },
  (): void => {
    authStore.dismissError()
  },
)
</script>

<template>
  <RouterView />
</template>
