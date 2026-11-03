<script setup lang="ts">
/**
 * The four things the schedule's `retainCount` alone would be read as promising and does not, one
 * row each, in reading order: retention is a periodic pruning pass, never removes a row whose archive
 * is still on the disk, exempts backups taken before an account was deleted, and the copy is written
 * to this server unencrypted. They are visible text rather than a doc comment because the operator
 * who picks a number is the person who would otherwise believe all four, and a spec asserts each.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import type { UiIconName } from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'

/** One caveat about what retention does not cover, with the glyph that marks its row. */
interface RetentionPoint {
  /** Stable key for the list row. */
  key: string
  /** The glyph marking the row; decorative, the sentence beside it says everything. */
  icon: UiIconName
  /** The caveat, already translated. */
  text: string
}

const { t } = useI18n()

/** The four caveats, in reading order. */
const retentionPoints: ComputedRef<readonly RetentionPoint[]> = computed(() => {
  return [
    { key: 'cadence', icon: 'clock', text: t('backups.schedule.retention.cadence') },
    { key: 'neverBlind', icon: 'hardDrive', text: t('backups.schedule.retention.neverBlind') },
    { key: 'preDeletion', icon: 'archive', text: t('backups.schedule.retention.preDeletion') },
    { key: 'storage', icon: 'lock', text: t('backups.schedule.retention.storage') },
  ]
})
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="t('backups.schedule.retention.heading')" />
    <ul class="flex flex-col gap-4">
      <li v-for="point in retentionPoints" :key="point.key" class="flex items-start gap-3">
        <UiIconTile :name="point.icon" size="sm" />
        <p class="min-w-0 pt-1 text-sm text-text-secondary break-words">
          {{ point.text }}
        </p>
      </li>
    </ul>
  </UiCard>
</template>
