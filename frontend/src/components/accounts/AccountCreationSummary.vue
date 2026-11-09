<script setup lang="ts">
/**
 * The panel beside the create-account form that says what pressing "Create account" will make on
 * the server (issue #194): the system user, its home directory and disk quota, the runtime
 * ceilings of the chosen plan, and the owner's login with its invitation. Static chrome only — it
 * describes the operation, and every value it would otherwise need (plan limits, names) is shown by
 * the form itself or by the account's own page afterwards.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { OwnerLoginMode } from '../../types/account'

const props = defineProps<{
  /** How the owner's login is made; the last item says which, so the panel never promises a mail it will not send. */
  loginMode: OwnerLoginMode
}>()

const { t } = useI18n()

/** The items listed, in the order the server performs them; each is a key under `accounts.form.summary.items`. */
const items: ComputedRef<string[]> = computed(() => {
  return ['systemUser', 'home', 'limits', props.loginMode === 'initialPassword' ? 'loginPassword' : 'login']
})
</script>

<template>
  <UiCard>
    <UiSectionHeading :title="t('accounts.form.summary.title')" :subtitle="t('accounts.form.summary.subtitle')" />
    <ul class="mt-3 flex list-disc flex-col gap-2 ps-5 text-sm text-text-secondary wrap-anywhere">
      <li v-for="item in items" :key="item">{{ t(`accounts.form.summary.items.${item}`) }}</li>
    </ul>
  </UiCard>
</template>
