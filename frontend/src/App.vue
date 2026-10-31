<script setup lang="ts">
/**
 * Root SPA shell: just the router-outlet. Holds no state, no header, and no
 * `<main>` of its own — every route nests under a layout
 * (`src/layouts/DefaultLayout.vue` or `AuthLayout.vue`), and rules/vue.md
 * requires exactly one `<main>` landmark per document, so this component
 * must not add a second one.
 *
 * **A message belongs to the screen that showed it (issue #221).** Each account flow keeps its
 * own failure message — sign-in (auth), first-run setup, the e-mailed password links, the
 * signed-in devices and the second factor — so a refused invitation followed by "Back to sign in"
 * must not greet the visitor with the same refusal on the sign-in screen. This is the one
 * component that sees every move between screens, in either layout, so it dismisses every flow's
 * message on each of them; no page clears one for itself.
 *
 * **What belongs to a signed-in identity leaves with it.** Stores never reach into one another
 * (rules/vue.md "State"), so when the auth store stops being signed in — a sign-out, a sign-out
 * everywhere, a refused renewal — this component clears the module catalogue and the devices list,
 * which a different identity signing in on the same tab must not inherit.
 */
import { watch } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from './stores/auth'
import { useModulesStore } from './stores/modules'
import { usePasswordLinksStore } from './stores/passwordLinks'
import { useSessionsStore } from './stores/sessions'
import { useSetupStore } from './stores/setup'
import { useTwoFactorStore } from './stores/twoFactor'

const route = useRoute()
const authStore = useAuthStore()
const modulesStore = useModulesStore()
const passwordLinksStore = usePasswordLinksStore()
const sessionsStore = useSessionsStore()
const setupStore = useSetupStore()
const twoFactorStore = useTwoFactorStore()

// Keyed on the path: a query change on the same screen (a token in a link, a filter) is not a new
// screen, and its message still belongs to it.
watch(
  (): string => {
    return route.path
  },
  (): void => {
    authStore.dismissError()
    setupStore.dismissError()
    passwordLinksStore.dismissError()
    sessionsStore.dismissError()
    twoFactorStore.dismissError()
  },
)

// Synchronous, so the clearing happens inside the sign-out itself, before the navigation that
// follows it can read a catalogue that belonged to the identity which just left.
watch(
  (): boolean => {
    return authStore.isAuthenticated
  },
  (isAuthenticated: boolean): void => {
    if (!isAuthenticated) {
      modulesStore.clear()
      sessionsStore.clear()
    }
  },
  { flush: 'sync' },
)
</script>

<template>
  <RouterView />
</template>
