<script setup lang="ts">
/**
 * The findings of a dry run, conflicts first, each with the sentence the panel wrote in the
 * administrator's language and what it is about. Severity is named in words and repeated in the
 * badge's tone, never by colour alone.
 *
 * **The cron notice stands apart.** Cron entries are always imported switched off (the target is not
 * live yet, and a job that ran twice would do what it does twice), and the panel says so as an `Info`
 * finding with a stable code. That finding is drawn as its own notice above the list, so it is not lost
 * among the routine ones; the code decides where it is drawn, the panel's sentence is its text.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import type { MigrationFinding, MigrationFindingSeverity } from '../../types/accountMove'

/** The code the panel gives the "cron entries were imported disabled" finding. */
const CRON_DISABLED_CODE = 'MigrationCronImportedDisabled'

/** The severities in the order they are drawn: what blocks first. */
const SEVERITY_ORDER: readonly MigrationFindingSeverity[] = ['Conflict', 'Warning', 'Info']

/** The tone of each severity's badge. */
const TONES: Readonly<Record<MigrationFindingSeverity, BadgeVariant>> = {
  Conflict: 'danger',
  Warning: 'warning',
  Info: 'info',
}

/** Props accepted by {@link MigrationFindingList}. */
const props = defineProps<{
  /** The findings to draw, in any order. */
  findings: MigrationFinding[]
}>()

const { t } = useI18n()

/** The cron notices, drawn above the list. */
const cronNotices: ComputedRef<MigrationFinding[]> = computed(() => {
  return props.findings.filter((finding) => {
    return finding.code === CRON_DISABLED_CODE
  })
})

/** Every other finding, conflicts first and otherwise in the order the panel gave. */
const ordered: ComputedRef<MigrationFinding[]> = computed(() => {
  return SEVERITY_ORDER.flatMap((severity) => {
    return props.findings.filter((finding) => {
      return finding.severity === severity && finding.code !== CRON_DISABLED_CODE
    })
  })
})
</script>

<template>
  <div class="flex min-w-0 flex-col gap-3">
    <UiAlert v-for="notice in cronNotices" :key="`${notice.moduleId}-${notice.subject}`" variant="info" data-testid="cron-disabled-notice">
      {{ notice.message }}
    </UiAlert>

    <ul v-if="ordered.length > 0" class="flex flex-col gap-2" :aria-label="t('accountMove.import.report.listLabel')">
      <li
        v-for="(finding, index) in ordered"
        :key="`${finding.moduleId}-${finding.code}-${finding.subject}-${index}`"
        class="flex flex-col gap-1 rounded-control border border-border-subtle bg-surface-2 px-3 py-2.5"
        :data-severity="finding.severity"
      >
        <div class="flex flex-wrap items-center gap-2">
          <UiBadge :variant="TONES[finding.severity]">{{ t(`accountMove.import.report.severities.${finding.severity}`) }}</UiBadge>
          <span class="text-xs text-text-muted">{{ finding.moduleId }}</span>
        </div>
        <p class="text-sm text-text-primary wrap-anywhere">{{ finding.message }}</p>
        <p v-if="finding.subject !== ''" class="font-mono text-sm text-text-muted wrap-anywhere">{{ finding.subject }}</p>
      </li>
    </ul>
  </div>
</template>
