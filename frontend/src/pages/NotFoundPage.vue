<script setup lang="ts">
/**
 * Catch-all 404 page for any path the router does not recognize. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark belongs to the
 * layout this page is nested under.
 */
import { useI18n } from 'vue-i18n'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiNavLink from '../components/ui/UiNavLink.vue'

const { t } = useI18n()
</script>

<template>
  <section class="mx-auto max-w-xl">
    <UiEmptyState :title="t('app.notFound.heading')" :description="t('app.notFound.description')">
      <UiNavLink :to="{ name: 'system-status' }">{{ t('app.notFound.backHome') }}</UiNavLink>
    </UiEmptyState>
  </section>
</template>
