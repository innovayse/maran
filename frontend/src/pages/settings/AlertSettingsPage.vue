<script setup lang="ts">
/**
 * Who is told about an alert and when: the recipient list and the thresholds. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under. Each card owns its own loading and saving, so a refused read of one never blanks
 * the other.
 */
import { useI18n } from 'vue-i18n'
import AlertRecipientsCard from '../../components/settings/AlertRecipientsCard.vue'
import AlertThresholdsCard from '../../components/settings/AlertThresholdsCard.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'

const { t } = useI18n()
</script>

<template>
  <section class="w-full">
    <UiPageHeading
      class="mb-4"
      :title="t('alertSettings.heading')"
      :subtitle="t('alertSettings.subtitle')"
    />

    <div class="grid grid-cols-1 items-start gap-4 lg:grid-cols-2">
      <AlertRecipientsCard />
      <AlertThresholdsCard />
    </div>
  </section>
</template>
