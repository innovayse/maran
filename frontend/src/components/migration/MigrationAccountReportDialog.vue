<script setup lang="ts">
/**
 * The comparison report of one account's cut-over: whether the final copy of the files converged, the size of
 * the source home, the databases that were loaded and what this panel's own web server answered for each site.
 * It states its limit in words: the files line is rsync's own convergence, not a second measurement of the home on
 * this server. Props in, events out.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiModal from '../ui/UiModal.vue'
import type { MigrationAccountReport } from '../../types/migrationJob'

/** Props accepted by {@link MigrationAccountReportDialog}. */
defineProps<{
  /** The report to draw, or `null` while the dialog is closed. */
  report: MigrationAccountReport | null
}>()

/** Events emitted by {@link MigrationAccountReportDialog}. */
const emit = defineEmits<{
  /** The administrator closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()
</script>

<template>
  <UiModal
    :open="report !== null"
    :title="t('migrationJobs.report.title', { name: report?.sourceAccountName ?? '' })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <div v-if="report !== null" class="flex min-w-0 flex-col gap-4" data-testid="account-report">
      <section class="flex flex-col gap-1">
        <h3 class="text-sm font-semibold">{{ t('migrationJobs.report.files') }}</h3>
        <p class="text-sm text-text-secondary">
          {{ report.filesConverged ? t('migrationJobs.report.converged', { slices: report.slices }) : t('migrationJobs.report.notCopied') }}
        </p>
        <p v-if="report.sourceBytes !== null" class="text-sm text-text-secondary">
          {{ t('migrationJobs.report.sourceBytes', { bytes: report.sourceBytes }) }}
        </p>
        <p class="text-xs text-text-muted">{{ t('migrationJobs.report.limit') }}</p>
      </section>
      <section class="flex flex-col gap-1">
        <h3 class="text-sm font-semibold">{{ t('migrationJobs.report.databases') }}</h3>
        <p v-if="report.databases.length === 0" class="text-sm text-text-secondary">{{ t('migrationJobs.report.noDatabases') }}</p>
        <ul v-else class="text-sm text-text-secondary">
          <li v-for="database in report.databases" :key="database.name">
            <code class="font-mono">{{ database.name }}</code> — {{ t('migrationJobs.report.dumpBytes', { bytes: database.dumpBytes }) }}
          </li>
        </ul>
      </section>
      <section class="flex flex-col gap-1">
        <h3 class="text-sm font-semibold">{{ t('migrationJobs.report.sites') }}</h3>
        <p v-if="report.sites.length === 0" class="text-sm text-text-secondary">{{ t('migrationJobs.report.noSites') }}</p>
        <ul v-else class="text-sm text-text-secondary">
          <li v-for="site in report.sites" :key="site.domain">
            <code class="font-mono">{{ site.domain }}</code> —
            {{ site.statusCode === null ? t('migrationJobs.report.noAnswer') : site.statusCode }}
            <span v-if="site.title">({{ site.title }})</span>
            <span class="ms-1 font-semibold">{{ site.serves ? t('migrationJobs.report.serves') : t('migrationJobs.report.doesNotServe') }}</span>
          </li>
        </ul>
      </section>
    </div>

    <template #footer>
      <UiButton @click="emit('close')">{{ t('common.close') }}</UiButton>
    </template>
  </UiModal>
</template>
