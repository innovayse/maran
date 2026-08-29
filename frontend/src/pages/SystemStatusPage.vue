<script setup lang="ts">
/**
 * System status page: the always-available, ungated route. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the
 * layout it is nested under (`DefaultLayout`), and a nested one would break
 * screen-reader landmark navigation. Shows whether the panel API
 * is reachable and healthy. Reads state exclusively from the system store
 * and triggers a check on mount — it never touches the API layer directly
 * (rules/vue.md: API composables are called from stores only).
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../components/ui/UiCard.vue'
import { useSystemStore } from '../stores/system'

const { t } = useI18n()
const store = useSystemStore()

/**
 * Kicks off the initial health check once the page is mounted.
 * @returns Resolves when the check has settled (successfully or not).
 */
const refresh = async (): Promise<void> => {
  await store.checkHealth()
}

onMounted(refresh)
</script>

<template>
  <section class="mx-auto max-w-xl">
    <h1 class="mb-4 text-2xl font-semibold">{{ t('app.status.heading') }}</h1>
    <UiCard>
      <!-- Healthy: the backend answered; interpolate its reported status. -->
      <p v-if="store.status !== null">{{ t('app.status.ok', { status: store.status }) }}</p>
      <!-- Backend answered with an error: its text is already localized
           server-side, render it verbatim (rules/vue.md). -->
      <p v-else-if="store.errorMessage !== null">{{ store.errorMessage }}</p>
      <!-- The request never reached the backend: the one case with no
           server-provided message, covered by a frontend-owned string. -->
      <p v-else-if="store.unreachable">{{ t('app.status.unreachable') }}</p>
    </UiCard>
  </section>
</template>
