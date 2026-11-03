<script setup lang="ts">
/**
 * The state of a recorded backup schedule: whether it is on, when it runs next and when it last
 * ran. The next run is omitted while nothing is coming (the schedule is switched off); a schedule
 * that never ran says so in words instead of showing an empty value.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { BackupSchedule } from '../../types/backupSchedule'

/** Props accepted by {@link BackupScheduleSummary}. */
const props = defineProps<{
  /** The schedule the panel reported for the scope; `null` while the panel reports none. */
  schedule: BackupSchedule | null
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** When the schedule last ran, or a placeholder saying it never has. */
const lastRun: ComputedRef<string> = computed(() => {
  const at = props.schedule?.lastRunAt ?? null
  return at === null
    ? t('backups.schedule.summary.neverRan')
    : formatIsoTimestamp(at, localeStore.current)
})

/** When the schedule will next run, or `null` while it is switched off and nothing is coming. */
const nextRun: ComputedRef<string | null> = computed(() => {
  const at = props.schedule?.nextRunAt ?? null
  return at === null ? null : formatIsoTimestamp(at, localeStore.current)
})
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="t('backups.schedule.summary.heading')" />
    <UiDescriptionList>
      <UiDescriptionItem :term="t('backups.schedule.summary.state')">
        <UiBadge :variant="schedule?.enabled === true ? 'success' : 'warning'">
          {{
            schedule?.enabled === true
              ? t('backups.schedule.summary.on')
              : t('backups.schedule.summary.off')
          }}
        </UiBadge>
      </UiDescriptionItem>
      <UiDescriptionItem v-if="nextRun !== null" :term="t('backups.schedule.summary.nextRun')" mono>
        {{ nextRun }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('backups.schedule.summary.lastRun')" mono>
        {{ lastRun }}
      </UiDescriptionItem>
    </UiDescriptionList>
  </UiCard>
</template>
