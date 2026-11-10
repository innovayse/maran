<script setup lang="ts">
/**
 * The dry run of a job: whether it can start, and every finding, conflicts first. A finding names the source
 * account it is about, how much it matters in words and in tone, and the backend's sentence in the caller's
 * language; this component only renders them. Props in, nothing out.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { MigrationJobReport } from '../../types/migrationJob'

/** Props accepted by {@link MigrationFindingsList}. */
defineProps<{
  /** The dry run to draw. */
  report: MigrationJobReport
}>()

const { t } = useI18n()

/** The tone of each severity; the text names it too, so colour is never the only carrier. */
const TONES: Readonly<Record<string, BadgeVariant>> = { Conflict: 'danger', Warning: 'warning', Info: 'info' }
</script>

<template>
  <div class="flex min-w-0 flex-col gap-3" data-testid="dry-run-report">
    <UiAlert :variant="report.canStart ? 'success' : 'error'">
      {{ report.canStart ? t('migrationJobs.dryRun.canStart') : t('migrationJobs.dryRun.hasConflicts') }}
    </UiAlert>
    <p v-if="report.findings.length === 0" class="text-sm text-text-secondary">{{ t('migrationJobs.dryRun.clean') }}</p>
    <ul v-else class="flex min-w-0 flex-col gap-2">
      <li
        v-for="(finding, index) in report.findings"
        :key="index"
        class="flex min-w-0 flex-wrap items-start gap-2 rounded-control border border-border-strong px-3 py-2 text-sm"
      >
        <UiBadge :variant="TONES[finding.severity] ?? 'neutral'">{{ t(`migrationJobs.dryRun.severity.${finding.severity}`) }}</UiBadge>
        <span class="font-semibold">{{ finding.sourceAccountName }}</span>
        <span class="min-w-0 flex-1 wrap-anywhere text-text-secondary">{{ finding.message }}</span>
        <code class="font-mono text-xs text-text-muted wrap-anywhere">{{ finding.subject }}</code>
      </li>
    </ul>
  </div>
</template>
