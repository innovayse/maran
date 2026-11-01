<script setup lang="ts">
/**
 * The panel beside the create-account form that says what pressing "Create account" will make on
 * the server (issue #194): the system user, its home directory and disk quota, the runtime
 * ceilings of the chosen plan, and the owner's login with its invitation. Static chrome only — it
 * describes the operation, and every value it would otherwise need (plan limits, names) is shown by
 * the form itself or by the account's own page afterwards.
 */
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'

/** The items listed, in the order the server performs them; each is a key under `accounts.form.summary.items`. */
const ITEMS = ['systemUser', 'home', 'limits', 'login'] as const

const { t } = useI18n()
</script>

<template>
  <UiCard>
    <UiSectionHeading :title="t('accounts.form.summary.title')" :subtitle="t('accounts.form.summary.subtitle')" />
    <ul class="mt-3 flex list-disc flex-col gap-2 ps-5 text-sm text-text-secondary wrap-anywhere">
      <li v-for="item in ITEMS" :key="item">{{ t(`accounts.form.summary.items.${item}`) }}</li>
    </ul>
  </UiCard>
</template>
