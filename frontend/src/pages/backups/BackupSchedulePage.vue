<script setup lang="ts">
/**
 * The backup schedule screen: when this server takes backups on its own, and how many it keeps.
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this
 * page is nested under. State comes exclusively from the schedules store; the page never touches
 * the API layer (rules/vue.md: API composables are called from stores only).
 *
 * **Two scopes, not "with an account" and "without one".** A schedule whose `accountId` is `null`
 * is the host-wide policy every account without an override is backed up by; a schedule naming an
 * account is that account's override. Those are different things, so the scope picker's first
 * entry names the host-wide policy in words rather than sitting empty as a placeholder — an
 * unselected picker would read as "no account chosen yet" and hide the fact that a real, running
 * policy is what was loaded.
 *
 * **A 404 on the first read is the normal state and is rendered as one.** The panel invents no
 * default schedule, so a server nobody has configured answers `404 BackupScheduleNotFound` for
 * ever. The screen says nothing is scheduled and leaves every field blank; it never shows a filled
 * form for a schedule that does not exist, which would be the screen lying about what the server
 * will do tonight.
 *
 * **What this screen must not imply.** Retention is a 5-minute pruning pass, not something that
 * happens when a backup is taken, and it never removes a row whose archive is still on the disk: a
 * destination it cannot address is skipped and counted, and any agent refusal other than "already
 * gone" keeps the row and stops the pass for that account. A backup taken before an account was
 * deleted is exempt from retention entirely and is bounded only by an administrator deleting it.
 * The copy is written to this server and is not encrypted at rest. Every one of those is said on
 * the screen, because `retainCount` on its own reads as a promise none of them keep.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiSwitch from '../../components/ui/UiSwitch.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useBackupSchedulesStore } from '../../stores/backupSchedules'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import { weekdayName } from '../../utils/weekdayName'
import type {
  BackupFrequency,
  BackupSchedule,
  BackupScheduleDay,
} from '../../types/backupSchedule'

/** The scope value standing for the host-wide policy, which is a `null` account on the wire. */
const HOST_WIDE = ''

/** The weekdays, in the order a week is read, as the backend names them on the wire. */
const WEEKDAYS: readonly BackupScheduleDay[] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
]

/** Problem codes that name one field, and the field each of them marks invalid. */
const FIELD_OF_CODE: Readonly<Record<string, string>> = {
  BackupScheduleHourOutOfRange: 'hour',
  BackupScheduleRetainOutOfRange: 'retain',
  BackupScheduleDayRequired: 'day',
  BackupScheduleDayNotAllowed: 'day',
}

const { t } = useI18n()
const store = useBackupSchedulesStore()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()

/** The scope being edited: {@link HOST_WIDE}, or an account's identity. */
const scope: Ref<string> = ref(HOST_WIDE)

/** How often a backup is taken, as the select binds it; empty while nothing is scheduled. */
const frequency: Ref<string> = ref('')

/** The hour of the day in UTC, as typed. Text, because the form is `novalidate` and the server bounds it. */
const hourUtc: Ref<string> = ref('')

/** The weekday a weekly schedule fires on, as the select binds it. */
const dayOfWeekUtc: Ref<string> = ref('')

/** How many successful backups to keep, as typed. */
const retainCount: Ref<string> = ref('')

/** Whether the schedule runs at all. */
const enabled: Ref<boolean> = ref(false)

/** The client's own message about a field the operator has not filled in, or `null`. */
const localError: Ref<string | null> = ref(null)

/** The account the scope names, or `null` for the host-wide policy. */
const scopeAccountId: ComputedRef<string | null> = computed(() => {
  return scope.value === HOST_WIDE ? null : scope.value
})

/** The scopes on offer: the host-wide policy first, then every account it can be overridden for. */
const scopeOptions: ComputedRef<readonly SelectOption[]> = computed(() => {
  return [
    { value: HOST_WIDE, label: t('backups.schedule.scope.hostWide') },
    ...accountsStore.accounts.map((account) => {
      return { value: account.id, label: account.name }
    }),
  ]
})

/** The two cadences, labelled in the interface language. */
const frequencyOptions: ComputedRef<readonly SelectOption[]> = computed(() => {
  return [
    { value: 'daily', label: t('backups.schedule.frequency.daily') },
    { value: 'weekly', label: t('backups.schedule.frequency.weekly') },
  ]
})

/** The weekdays, named in the interface language by the date library rather than by this panel. */
const dayOptions: ComputedRef<readonly SelectOption[]> = computed(() => {
  return WEEKDAYS.map((day) => {
    return { value: day, label: weekdayName(day, localeStore.current) }
  })
})

/** Whether the day picker applies at all — a daily schedule has no weekday and must send none. */
const isWeekly: ComputedRef<boolean> = computed(() => {
  return frequency.value === 'weekly'
})

/** True when the panel answered and reported that this scope has no schedule at all. */
const isUnscheduled: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && !store.isConfigured && store.errorMessage === null
})

/** When the schedule last ran, or a placeholder saying it never has. */
const lastRun: ComputedRef<string> = computed(() => {
  const at = store.schedule?.lastRunAt ?? null
  return at === null
    ? t('backups.schedule.summary.neverRan')
    : formatIsoTimestamp(at, localeStore.current)
})

/** The message for the banner: the last failure, unless a field is already showing it. */
const bannerError: ComputedRef<string | null> = computed(() => {
  return store.errorCode in FIELD_OF_CODE ? null : store.errorMessage
})

/**
 * The server's message for one field, or `null` when the last failure was not about it.
 *
 * The message is the backend's own, already localized, and it is rendered in exactly one place: on
 * the field the code names, or in the banner when the code names none. Showing it twice would make
 * one refusal look like two.
 * @param field The field being drawn.
 * @returns The message to show under it, or `null`.
 */
const fieldError = (field: string): string | null => {
  return FIELD_OF_CODE[store.errorCode] === field ? store.errorMessage : null
}

/**
 * Copies a schedule the panel reported into the form's fields, or blanks them when there is none.
 *
 * Blank rather than a plausible default when there is none: this panel invents no schedule, and a
 * form pre-filled with "daily at 03:00" would be a proposal the screen presents as the state of the
 * server.
 * @param value The schedule to show, or `null` when the scope has none.
 * @returns Nothing; the fields are updated synchronously.
 */
const fill = (value: BackupSchedule | null): void => {
  localError.value = null
  if (value === null) {
    frequency.value = ''
    hourUtc.value = ''
    dayOfWeekUtc.value = ''
    retainCount.value = ''
    enabled.value = false
    return
  }

  frequency.value = value.frequency
  hourUtc.value = String(value.hourUtc)
  dayOfWeekUtc.value = value.dayOfWeekUtc ?? ''
  retainCount.value = String(value.retainCount)
  enabled.value = value.enabled
}

/**
 * Names the field the operator has left empty, if any.
 *
 * This mirrors the server's required-ness and nothing more: the ranges are checked by the
 * validator the backend owns, and a second copy of those bounds here would be a rule that can
 * drift out of step with the one that actually decides. What it does buy is that a blank field is
 * not sent as a zero the server would have to refuse.
 * @returns The translated complaint, or `null` when there is nothing to complain about.
 */
const missingField = (): string | null => {
  if (frequency.value === '') {
    return t('backups.schedule.errors.frequencyRequired')
  }
  if (hourUtc.value.trim() === '') {
    return t('backups.schedule.errors.hourRequired')
  }
  if (retainCount.value.trim() === '') {
    return t('backups.schedule.errors.retainRequired')
  }
  if (isWeekly.value && dayOfWeekUtc.value === '') {
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
  if (localError.value !== null) {
    return
  }

  await store.save({
    accountId: scopeAccountId.value,
    // Always the server's default destination. The panel records no other, and the endpoint that
    // would record one refuses every request today, so there is nothing here to choose from.
    destinationId: null,
    // The two selects bind plain strings by contract; their options are exactly the wire values,
    // so these narrow rather than guess.
    frequency: frequency.value as BackupFrequency,
    hourUtc: Number(hourUtc.value),
    // A daily schedule must send no weekday at all — the server refuses one with
    // `BackupScheduleDayNotAllowed` — so the picker's last value is dropped rather than kept.
    dayOfWeekUtc: isWeekly.value ? (dayOfWeekUtc.value as BackupScheduleDay) : null,
    retainCount: Number(retainCount.value),
    enabled: enabled.value,
  })
}

// The form follows whatever the panel last reported for the scope, including after a save.
watch(
  () => {
    return store.schedule
  },
  fill,
  { immediate: true },
)

// A change of scope is a different schedule, so it is read again rather than edited in place.
watch(
  scopeAccountId,
  (accountId) => {
    void store.load(accountId)
  },
  { immediate: true },
)

void accountsStore.load()
</script>

<template>
  <section class="w-full max-w-2xl">
    <UiPageHeading
      class="mb-4"
      :title="t('backups.schedule.heading')"
      :subtitle="t('backups.schedule.subtitle')"
      :note="t('backups.schedule.utcNote')"
    />

    <UiCard class="mb-4">
      <UiSelect
        v-model="scope"
        :label="t('backups.schedule.scope.label')"
        :options="scopeOptions"
        :disabled="store.loading || store.saving"
      />
      <p class="mt-2 text-sm text-text-secondary">
        {{
          scopeAccountId === null
            ? t('backups.schedule.scope.hostWideHint')
            : t('backups.schedule.scope.overrideHint')
        }}
      </p>
    </UiCard>

    <UiAlert v-if="bannerError !== null" variant="error" class="mb-4">{{ bannerError }}</UiAlert>

    <UiAlert v-if="localError !== null" variant="error" class="mb-4">{{ localError }}</UiAlert>

    <UiAlert v-if="store.saved" variant="info" class="mb-4">
      {{ t('backups.schedule.saved') }}
    </UiAlert>

    <UiSpinner v-if="store.loading" :label="t('backups.schedule.loading')" />

    <template v-else-if="store.isLoaded">
      <UiAlert v-if="isUnscheduled" variant="info" class="mb-4">
        {{ t('backups.schedule.unscheduled') }}
      </UiAlert>

      <UiCard v-else-if="store.isConfigured" class="mb-4">
        <UiSectionHeading class="mb-3" :title="t('backups.schedule.summary.heading')" />
        <UiDescriptionList>
          <UiDescriptionItem :term="t('backups.schedule.summary.state')">
            {{ store.schedule?.enabled === true ? t('backups.schedule.summary.on') : t('backups.schedule.summary.off') }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('backups.schedule.summary.lastRun')" mono>
            {{ lastRun }}
          </UiDescriptionItem>
        </UiDescriptionList>
      </UiCard>

      <UiCard class="mb-4">
        <UiSectionHeading
          class="mb-3"
          :title="t('backups.schedule.form.heading')"
          :subtitle="t('backups.schedule.form.subtitle')"
        />
        <UiForm @submit="submit">
          <div class="flex flex-col gap-3">
            <UiSelect
              v-model="frequency"
              :label="t('backups.schedule.form.frequencyLabel')"
              :placeholder="t('backups.schedule.form.frequencyPlaceholder')"
              :options="frequencyOptions"
              required
            />

            <UiSelect
              v-if="isWeekly"
              v-model="dayOfWeekUtc"
              :label="t('backups.schedule.form.dayLabel')"
              :placeholder="t('backups.schedule.form.dayPlaceholder')"
              :options="dayOptions"
              :error="fieldError('day')"
              required
            />

            <UiInput
              v-model="hourUtc"
              :label="t('backups.schedule.form.hourLabel')"
              :placeholder="t('backups.schedule.form.hourPlaceholder')"
              :error="fieldError('hour')"
              required
            />

            <UiInput
              v-model="retainCount"
              :label="t('backups.schedule.form.retainLabel')"
              :placeholder="t('backups.schedule.form.retainPlaceholder')"
              :error="fieldError('retain')"
              required
            />

            <UiSwitch v-model="enabled" :label="t('backups.schedule.form.enabledLabel')" />
            <p class="text-sm text-text-secondary">{{ t('backups.schedule.form.enabledHint') }}</p>

            <UiButton class="mt-1" type="submit" :disabled="store.saving">
              {{ store.saving ? t('common.saving') : t('backups.schedule.form.submit') }}
            </UiButton>
          </div>
        </UiForm>
      </UiCard>

      <UiCard>
        <UiSectionHeading class="mb-3" :title="t('backups.schedule.retention.heading')" />
        <!-- Every one of these is something `retainCount` on its own would be read as promising and
             does not. They are on the screen rather than in a doc comment because the operator who
             types a number is the person who would otherwise believe all four. -->
        <p class="text-sm text-text-secondary">{{ t('backups.schedule.retention.cadence') }}</p>
        <p class="mt-2 text-sm text-text-secondary">{{ t('backups.schedule.retention.neverBlind') }}</p>
        <p class="mt-2 text-sm text-text-secondary">{{ t('backups.schedule.retention.preDeletion') }}</p>
        <p class="mt-2 text-sm text-text-secondary">{{ t('backups.schedule.retention.storage') }}</p>
      </UiCard>
    </template>
  </section>
</template>
