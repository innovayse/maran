<script setup lang="ts">
/**
 * The form that states a backup schedule: cadence, weekday (weekly only), hour (UTC), how many
 * copies to keep, and whether it runs. The fields are v-models the page owns; this component only
 * draws them and reports a submit.
 *
 * **Nothing here is typed.** Every choice is a select over a list the panel supplies (held by the
 * backup options store), so a value outside what the server's validator accepts cannot be written;
 * the validator still decides, and its refusal arrives as the `*Error` props and is shown on the
 * field it names. Layout switches read the width of the box that holds the form (a container
 * query), not the viewport's, so a large system font cannot squeeze a select.
 */
import { computed, type ComputedRef, type WritableComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiForm from '../ui/UiForm.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import { useBackupOptionsStore } from '../../stores/backupOptions'
import {
  BACKUP_SCHEDULE_DAYS,
  type BackupFrequencyWire,
  type BackupScheduleDay,
} from '../../types/backupSchedule'

/** Props accepted by {@link BackupScheduleForm}. */
defineProps<{
  /** Whether the scope's schedule is being read; the form keeps its labels over placeholder controls. */
  loading: boolean
  /** Whether a save is in flight; the submit button is disabled and says so. */
  saving: boolean
  /** The server's message about the weekday, or `null` when the last failure was not about it. */
  dayError: string | null
  /** The server's message about the hour, or `null` when the last failure was not about it. */
  hourError: string | null
  /** The server's message about the retention count, or `null` when the last failure was not about it. */
  retainError: string | null
}>()

/** Emits accepted from {@link BackupScheduleForm}. */
const emit = defineEmits<{
  /** The operator pressed save; the page validates and sends. */
  submit: []
}>()

/** How often a backup is taken; `null` while none has been chosen. */
const frequency = defineModel<BackupFrequencyWire | null>('frequency', { required: true })

/** The hour of the day in UTC as chosen, or the empty string while none is. */
const hourUtc = defineModel<string>('hourUtc', { required: true })

/** The weekday a weekly schedule fires on; `null` until one is chosen. */
const dayOfWeekUtc = defineModel<BackupScheduleDay | null>('dayOfWeekUtc', { required: true })

/** How many successful backups to keep as chosen, or the empty string while none is. */
const retainCount = defineModel<string>('retainCount', { required: true })

/** Whether the schedule runs at all. */
const enabled = defineModel<boolean>('enabled', { required: true })

const { t } = useI18n()
const optionsStore = useBackupOptionsStore()

/** The cadence as its select binds it: the wire value, or the empty string while none is chosen. */
const frequencyOption: WritableComputedRef<string> = computed({
  get: (): string => {
    return frequency.value ?? ''
  },
  set: (value: string): void => {
    // Only a cadence the panel listed is accepted; the backend's validator still has the last word.
    const listed = optionsStore.options.frequencies.find((candidate) => {
      return candidate.value === value
    })?.value
    // The request contract names the two cadences the backend enum has (types/backupSchedule.ts).
    frequency.value = listed === 'daily' || listed === 'weekly' ? listed : null
  },
})

/** The weekday as its select binds it: the wire value, or the empty string while none is chosen. */
const dayOption: WritableComputedRef<string> = computed({
  get: (): string => {
    return dayOfWeekUtc.value ?? ''
  },
  set: (value: string): void => {
    dayOfWeekUtc.value = BACKUP_SCHEDULE_DAYS.find((candidate) => {
      return candidate === value
    }) ?? null
  },
})

/** The cadences, named by the panel in the interface language. */
const frequencyOptions: ComputedRef<readonly SelectOption[]> = computed(() => {
  return optionsStore.options.frequencies
})

/** The weekdays, named by the panel in the interface language. */
const dayOptions: ComputedRef<readonly SelectOption[]> = computed(() => {
  return optionsStore.options.weekdays
})

/** The hours of the day (UTC) the panel offers. */
const hourOptions: ComputedRef<readonly SelectOption[]> = computed(() => {
  return optionsStore.options.hours
})

/** How many copies the panel lets a schedule keep. */
const retainOptions: ComputedRef<readonly SelectOption[]> = computed(() => {
  return optionsStore.options.retainCounts
})

/** Whether the day picker applies at all — a daily schedule has no weekday and must send none. */
const isWeekly: ComputedRef<boolean> = computed(() => {
  return frequency.value === 'weekly'
})

/**
 * Reports a submit to the page.
 * @returns Nothing.
 */
const submit = (): void => {
  emit('submit')
}
</script>

<template>
  <UiCard class="@container">
    <UiSectionHeading
      class="mb-4"
      :title="t('backups.schedule.form.heading')"
      :subtitle="t('backups.schedule.form.subtitle')"
    />
    <UiForm :loading="loading" @submit="submit">
      <div class="flex flex-col gap-4">
        <div class="grid gap-4 @lg:grid-cols-2">
          <UiSelect
            v-model="frequencyOption"
            class="min-w-0"
            :label="t('backups.schedule.form.frequencyLabel')"
            :placeholder="t('backups.schedule.form.frequencyPlaceholder')"
            :options="frequencyOptions"
            :load-error="optionsStore.errorMessage"
            required
          />

          <UiSelect
            v-if="isWeekly"
            v-model="dayOption"
            class="min-w-0"
            :label="t('backups.schedule.form.dayLabel')"
            :placeholder="t('backups.schedule.form.dayPlaceholder')"
            :options="dayOptions"
            :load-error="optionsStore.errorMessage"
            :error="dayError"
            required
          />
        </div>

        <div class="grid gap-4 @lg:grid-cols-2">
          <UiSelect
            v-model="hourUtc"
            class="min-w-0"
            :label="t('backups.schedule.form.hourLabel')"
            :placeholder="t('backups.schedule.form.hourPlaceholder')"
            :options="hourOptions"
            :load-error="optionsStore.errorMessage"
            :error="hourError"
            required
          />

          <UiSelect
            v-model="retainCount"
            class="min-w-0"
            :label="t('backups.schedule.form.retainLabel')"
            :placeholder="t('backups.schedule.form.retainPlaceholder')"
            :options="retainOptions"
            :load-error="optionsStore.errorMessage"
            :error="retainError"
            required
          />
        </div>

        <div class="flex flex-col gap-2 rounded-control border border-border-subtle bg-surface-2 p-4">
          <UiSwitch v-model="enabled" :label="t('backups.schedule.form.enabledLabel')" />
          <p class="text-sm text-text-muted break-words">{{ t('backups.schedule.form.enabledHint') }}</p>
        </div>

        <UiButton class="w-full @lg:w-auto @lg:self-start" type="submit" :disabled="saving">
          {{ saving ? t('common.saving') : t('backups.schedule.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
