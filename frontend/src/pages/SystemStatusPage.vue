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
import UiAlert from '../components/ui/UiAlert.vue'
import UiCard from '../components/ui/UiCard.vue'
import UiSpinner from '../components/ui/UiSpinner.vue'
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
  <section class="w-full">
    <h1 class="mb-4 text-3xl font-semibold tracking-title text-text-primary">
      {{ t('app.status.heading') }}
    </h1>

    <!-- Healthy: the backend answered; interpolate its reported status. -->
    <UiCard v-if="store.status !== null">
      <p>{{ t('app.status.ok', { status: store.status }) }}</p>
    </UiCard>

    <!-- Backend answered with an error: its text is already localized
         server-side, render it verbatim (rules/vue.md). A failure gets the
         panel's error treatment here exactly as it does on every other
         screen, rather than reading as ordinary card copy. -->
    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <!-- The request never reached the backend: the one case with no
         server-provided message, covered by a frontend-owned string. -->
    <UiAlert v-else-if="store.unreachable" variant="error">{{ t('app.status.unreachable') }}</UiAlert>

    <!-- Nothing has settled yet. The store keeps no loading flag, so the
         pending state is "no verdict of any kind" — without this branch the
         first paint of the page is a blank panel. -->
    <UiSpinner v-else :label="t('app.status.checking')" />
  </section>
</template>
