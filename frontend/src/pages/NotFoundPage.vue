<script setup lang="ts">
/**
 * Catch-all 404 page for any path the router does not recognize. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark belongs to the
 * layout this page is nested under.
 *
 * The empty state IS this page, so it is asked for `titleAs="h1"`: the title
 * it draws is the page's only title, and without the promotion this is the
 * one routed screen in the panel whose content contributes no heading to the
 * document outline. Every other caller of that component sits under a page
 * heading and leaves the prop at its default.
 */
import { useI18n } from 'vue-i18n'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiNavLink from '../components/ui/UiNavLink.vue'

const { t } = useI18n()
</script>

<template>
  <section class="w-full">
    <UiEmptyState
      title-as="h1"
      :title="t('app.notFound.heading')"
      :description="t('app.notFound.description')"
    >
      <UiNavLink :to="{ name: 'system-status' }">{{ t('app.notFound.backHome') }}</UiNavLink>
    </UiEmptyState>
  </section>
</template>
