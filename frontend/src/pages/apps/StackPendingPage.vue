<script setup lang="ts">
/**
 * The screen the stack guard holds every non-administrator on while the host has no site web
 * server (issue #517). They cannot fix it, and every other screen would only answer with the
 * panel's `apps.stackNotReady` refusal, so this says plainly what is happening instead. Renders a
 * `<section>`; the layout owns the `<main>`.
 */
import { useI18n } from 'vue-i18n'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'

const { t } = useI18n()
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading :title="t('apps.pending.heading')" />
    <UiEmptyState :title="t('apps.pending.title')" :description="t('apps.pending.description')" />
  </section>
</template>
