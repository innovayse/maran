<script setup lang="ts">
/**
 * The refusal shown when an app cannot be removed because sites still use it: the panel's sentence,
 * and the domains of those sites as a list, so the administrator knows what to move or delete first.
 * The sentence and the domains are the panel's, printed as given as plain text.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import type { HostAppInUse } from '../../types/hostApp'

/** Props accepted by {@link HostAppInUseAlert}. */
defineProps<{
  /** The app's name as a reader reads it. */
  name: string
  /** The refusal, with the sites that use the app. */
  refusal: HostAppInUse
}>()

const { t } = useI18n()
</script>

<template>
  <UiAlert variant="error" data-testid="apps-in-use">
    <p class="font-semibold">{{ t('apps.inUse.title', { name }) }}</p>
    <p>{{ refusal.message }}</p>
    <template v-if="refusal.sites.length > 0">
      <p class="mt-2 text-sm">{{ t('apps.inUse.sites') }}</p>
      <ul class="list-disc pl-5 font-mono text-sm">
        <li v-for="site in refusal.sites" :key="site" class="wrap-anywhere">{{ site }}</li>
      </ul>
    </template>
  </UiAlert>
</template>
