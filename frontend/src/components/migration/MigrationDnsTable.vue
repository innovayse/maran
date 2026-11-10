<script setup lang="ts">
/**
 * The DNS helper's answer for a job: where each domain resolves now, from this server, and whether any address is
 * one of this server's. It reads A and AAAA records only; the written advice to lower the TTL before the cut-over
 * and to move the records after it stands beside the table, because the system resolver does not report a TTL.
 * Props in, nothing out.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import type { MigrationDnsAdvice } from '../../types/migrationJob'

/** Props accepted by {@link MigrationDnsTable}. */
defineProps<{
  /** The advice to draw. */
  advice: MigrationDnsAdvice
}>()

const { t } = useI18n()
</script>

<template>
  <div class="flex min-w-0 flex-col gap-3" data-testid="dns-advice">
    <UiAlert variant="info">{{ t('migrationJobs.dns.ttlAdvice') }}</UiAlert>
    <p class="text-sm text-text-secondary">
      {{ t('migrationJobs.dns.thisServer', { addresses: advice.thisServerAddresses.join(', ') }) }}
    </p>
    <ul class="flex min-w-0 flex-col gap-1 text-sm">
      <li v-for="row in advice.rows" :key="row.domain" class="flex min-w-0 flex-wrap items-baseline gap-2">
        <code class="font-mono wrap-anywhere">{{ row.domain }}</code>
        <span class="text-text-secondary wrap-anywhere">
          {{ row.resolved ? row.addresses.join(', ') : t('migrationJobs.dns.unresolved') }}
        </span>
        <span class="font-semibold">{{ row.pointsHere ? t('migrationJobs.dns.pointsHere') : t('migrationJobs.dns.pointsElsewhere') }}</span>
      </li>
    </ul>
  </div>
</template>
