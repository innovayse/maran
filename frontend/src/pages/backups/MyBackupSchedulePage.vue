<script setup lang="ts">
/**
 * The customer's own backup schedule: when their account is backed up on its own, and how many
 * copies are kept. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the
 * layout this page is nested under. State comes exclusively from the schedule store; the page never
 * touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * **There is one schedule and no scope picker.** The operator's page (`BackupSchedulePage`) edits a
 * host-wide policy or any account's override; this page edits the signed-in customer's own row and
 * names no account, because the server takes the account from the login. A destination is not asked
 * for either: where an archive is written is the operator's decision.
 *
 * **A schedule nobody has saved is the ordinary first state and is rendered as one.** The panel
 * invents no default, so an account with no schedule of its own answers `schedule: null` in a normal
 * `200`; the page says so and leaves every field blank. It does not claim the account is not backed
 * up at all, because the server's own policy may still cover it.
 *
 * **Nothing here is typed.** The cadence, weekday, hour (UTC) and number of copies are selects over
 * lists the panel supplies (`GET /api/v1/backup-options`: the customer's shorter lists), so a value
 * outside what the server accepts cannot be written; its validator still decides, and its refusal is
 * shown on the field it names.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import BackupRetentionCard from '../../components/backups/BackupRetentionCard.vue'
import BackupScheduleForm from '../../components/backups/BackupScheduleForm.vue'
import BackupScheduleSummary from '../../components/backups/BackupScheduleSummary.vue'
import { useReloadOnLocale } from '../../composables/useReloadOnLocale'
import { useBackupOptionsStore } from '../../stores/backupOptions'
import { useMyBackupScheduleStore } from '../../stores/myBackupSchedule'
import type { BackupFrequencyWire, BackupSchedule, BackupScheduleDay } from '../../types/backupSchedule'

/** Problem codes that name one field, and the field each of them marks invalid. */
const FIELD_OF_CODE: Readonly<Record<string, string>> = {
  BackupScheduleHourOutOfRange: 'hour',
  BackupScheduleCustomerRetainTooLow: 'retain',
  BackupScheduleCustomerRetainTooHigh: 'retain',
  BackupScheduleDayRequired: 'day',
  BackupScheduleDayNotAllowed: 'day',
}

const { t } = useI18n()
const store = useMyBackupScheduleStore()
const optionsStore = useBackupOptionsStore()

/** How often a backup is taken; `null` while nothing is scheduled or none has been chosen. */
const frequency: Ref<BackupFrequencyWire | null> = ref(null)

/** The hour of the day in UTC as chosen, or the empty string while none is. The server bounds it. */
const hourUtc: Ref<string> = ref('')

/** The weekday a weekly schedule fires on; `null` until one is chosen. */
const dayOfWeekUtc: Ref<BackupScheduleDay | null> = ref(null)

/** How many successful backups to keep as chosen, or the empty string while none is. */
const retainCount: Ref<string> = ref('')

/** Whether the schedule runs at all. */
const enabled: Ref<boolean> = ref(false)

/** The client's own message about a field left empty, or `null`. */
const localError: Ref<string | null> = ref(null)

/** Whether the day picker applies at all — a daily schedule has no weekday and must send none. */
const isWeekly: ComputedRef<boolean> = computed(() => {
  return frequency.value === 'weekly'
})

/** True when the panel answered and reported that this account has no schedule of its own. */
const isUnscheduled: ComputedRef<boolean> = computed(() => {
  return store.loadState === 'loaded' && !store.isConfigured
})

/**
 * The message for the banner: the last refused save, unless a field is already showing it. A refused
 * read is the loading region's own error, so it is not repeated here.
 */
const bannerError: ComputedRef<string | null> = computed(() => {
  if (store.loadState !== 'loaded') {
    return null
  }
  return store.errorCode in FIELD_OF_CODE ? null : store.errorMessage
})

/**
 * The server's message for one field, or `null` when the last failure was not about it. It is the
 * backend's own, already localized, and is rendered in exactly one place: on the field its code
 * names, or in the banner when the code names none.
 * @param field The field being drawn.
 * @returns The message to show under it, or `null`.
 */
const fieldError = (field: string): string | null => {
  return FIELD_OF_CODE[store.errorCode] === field ? store.errorMessage : null
}

/**
 * Copies the schedule the panel reported into the form's fields, or blanks them when there is none.
 * Blank rather than a plausible default: this panel invents no schedule.
 * @param value The schedule to show, or `null` when the account has none.
 * @returns Nothing; the fields are updated synchronously.
 */
const fill = (value: BackupSchedule | null): void => {
  localError.value = null
  if (value === null) {
    frequency.value = null
    hourUtc.value = ''
    dayOfWeekUtc.value = null
    retainCount.value = ''
    enabled.value = false
    return
  }

  frequency.value = value.frequency
  hourUtc.value = String(value.hourUtc)
  dayOfWeekUtc.value = value.dayOfWeekUtc
  retainCount.value = String(value.retainCount)
  enabled.value = value.enabled
}

/**
 * Names the field left empty, if any. It mirrors the server's required-ness and nothing more: the
 * ranges are the validator's, and a copy of them here could drift out of step with the one that
 * decides.
 * @returns The translated complaint, or `null` when there is nothing to complain about.
 */
const missingField = (): string | null => {
  if (frequency.value === null) {
    return t('backups.schedule.errors.frequencyRequired')
  }
  if (hourUtc.value.trim() === '') {
    return t('backups.schedule.errors.hourRequired')
  }
  if (retainCount.value.trim() === '') {
    return t('backups.schedule.errors.retainRequired')
  }
  if (isWeekly.value && dayOfWeekUtc.value === null) {
    return t('backups.schedule.errors.dayRequired')
  }
  return null
}

/**
 * Saves the schedule as the form states it.
 * @returns Resolves once the request has settled, or immediately when a field is blank.
 */
const submit = async (): Promise<void> => {
  localError.value = missingField()
  const chosenFrequency = frequency.value
  if (localError.value !== null || chosenFrequency === null) {
    return
  }

  await store.save({
    frequency: chosenFrequency,
    hourUtc: Number(hourUtc.value),
    // A daily schedule must send no weekday — the server refuses one — so the picker's last value
    // is dropped rather than kept.
    dayOfWeekUtc: isWeekly.value ? dayOfWeekUtc.value : null,
    retainCount: Number(retainCount.value),
    enabled: enabled.value,
  })
}

// The form follows whatever the panel last reported, including after a save.
watch(
  () => {
    return store.schedule
  },
  fill,
  { immediate: true },
)

// The cadences, weekdays, hours and counts are named by the panel in the interface language, so they
// are read when the screen opens and again when the language changes; the schedule is read with them.
useReloadOnLocale(async (): Promise<void> => {
  await Promise.all([optionsStore.load(), store.load()])
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :title="t('backups.mySchedule.heading')"
      :subtitle="t('backups.mySchedule.subtitle')"
      :note="t('backups.schedule.utcNote')"
    />

    <UiAlert v-if="bannerError !== null" variant="error">{{ bannerError }}</UiAlert>

    <UiAlert v-if="localError !== null" variant="error">{{ localError }}</UiAlert>

    <UiAlert v-if="store.saved" variant="success">{{ t('backups.schedule.saved') }}</UiAlert>

    <!-- Amber: an account with no schedule of its own is backed up only if the server's policy
         covers it, which is a condition to act on rather than a fact to note. -->
    <UiAlert v-if="isUnscheduled" variant="warning">{{ t('backups.mySchedule.unscheduled') }}</UiAlert>

    <UiLoadingRegion :state="store.loadState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <div class="@container">
        <div class="grid items-start gap-4 @4xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <BackupScheduleForm
            v-model:frequency="frequency"
            v-model:hour-utc="hourUtc"
            v-model:day-of-week-utc="dayOfWeekUtc"
            v-model:retain-count="retainCount"
            v-model:enabled="enabled"
            audience="customer"
            :loading="store.loadState === 'loading'"
            :saving="store.saving"
            :day-error="fieldError('day')"
            :hour-error="fieldError('hour')"
            :retain-error="fieldError('retain')"
            @submit="submit"
          />

          <div class="flex min-w-0 flex-col gap-4">
            <BackupScheduleSummary v-if="store.isConfigured" :schedule="store.schedule" />

            <BackupRetentionCard />
          </div>
        </div>
      </div>
    </UiLoadingRegion>
  </section>
</template>
