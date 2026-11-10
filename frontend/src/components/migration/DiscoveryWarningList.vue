<script setup lang="ts">
/**
 * What discovery could not read or does not carry, one line per warning: the panel's own sentence
 * (already localized) and, in monospace, the account, site or database it is about. The subject is
 * source data and is shown as received. Nothing is drawn when there are no warnings.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { DiscoveryWarning } from '../../types/migrationSource'

/** Props accepted by {@link DiscoveryWarningList}. */
defineProps<{
  /** The warnings of the last discovery. */
  warnings: DiscoveryWarning[]
}>()

const { t } = useI18n()
</script>

<template>
  <div v-if="warnings.length > 0" class="flex min-w-0 flex-col gap-3" data-testid="discovery-warnings">
    <UiSectionHeading :title="t('migration.accounts.warningsTitle')" :subtitle="t('migration.accounts.warningsSubtitle')" />
    <UiAlert variant="warning">
      <ul class="flex flex-col gap-1">
        <li v-for="(warning, index) in warnings" :key="`${warning.kind}-${warning.subject}-${index}`">
          {{ warning.message }}
          <span v-if="warning.subject" class="ml-1 font-mono text-text-muted wrap-anywhere">{{ warning.subject }}</span>
        </li>
      </ul>
    </UiAlert>
  </div>
</template>
