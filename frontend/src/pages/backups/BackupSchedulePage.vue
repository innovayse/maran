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
 *
 * **Layout (redesign 2026-10).** The page header carries the module name as its eyebrow; whatever
 * needs attention (a refusal, a blank field, the save result, and an unscheduled scope as an amber
 * warning) comes before the controls; then the scope card, and below it the form beside a column
 * holding the summary and the four retention caveats. Under 1024px that column stacks under the
 * form. The retention caveats stay visible text, one per row, because a spec asserts each of them.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiForm from '../../components/ui/UiForm.vue'
import type { UiIconName } from '../../components/ui/UiIcon.vue'
import UiIconTile from '../../components/ui/UiIconTile.vue'
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

/** One caveat about what retention does not cover, with the glyph that marks its row. */
interface RetentionPoint {
  /** Stable key for the list row. */
  key: string
  /** The glyph marking the row; decorative, the sentence beside it says everything. */
  icon: UiIconName
  /** The caveat, already translated. */
  text: string
}

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

/** The four things `retainCount` alone would be read as promising and does not, in reading order. */
const retentionPoints: ComputedRef<readonly RetentionPoint[]> = computed(() => {
  return [
    { key: 'cadence', icon: 'clock', text: t('backups.schedule.retention.cadence') },
    { key: 'neverBlind', icon: 'hardDrive', text: t('backups.schedule.retention.neverBlind') },
    { key: 'preDeletion', icon: 'archive', text: t('backups.schedule.retention.preDeletion') },
    { key: 'storage', icon: 'lock', text: t('backups.schedule.retention.storage') },
  ]
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
  <section class="flex w-full flex-col gap-4">
    <!-- The eyebrow is the module's own name, so it reuses the list screen's heading: the same word
         the sidebar and the previous screen use, and no new text for the three languages to carry. -->
    <UiPageHeading
      :eyebrow="t('backups.list.heading')"
      :title="t('backups.schedule.heading')"
      :subtitle="t('backups.schedule.subtitle')"
      :note="t('backups.schedule.utcNote')"
    />

    <!-- What needs attention comes before the controls: a refusal, a field left blank, the result
         of the last save, and the fact that this scope is not being backed up at all. -->
    <UiAlert v-if="bannerError !== null" variant="error">{{ bannerError }}</UiAlert>

    <UiAlert v-if="localError !== null" variant="error">{{ localError }}</UiAlert>

    <UiAlert v-if="store.saved" variant="success">{{ t('backups.schedule.saved') }}</UiAlert>

    <!-- Amber, not blue: an unscheduled server takes no backup of its own accord, which is a
         condition to act on rather than a fact to note. -->
    <UiAlert v-if="!store.loading && isUnscheduled" variant="warning">
      {{ t('backups.schedule.unscheduled') }}
    </UiAlert>

    <!-- Every layout switch on this page reads the width of the box that holds the content
         (a container query), not the viewport's: under a large system font the sidebar and the
         text grow, the content panel is left ~700px wide, and the viewport still says "desktop" —
         a viewport `lg:` grid then squeezed the frequency select to a third of its needed width. -->
    <UiCard class="@container">
      <div class="flex flex-col gap-4 @2xl:flex-row @2xl:items-end @2xl:justify-between @2xl:gap-8">
        <p class="min-w-0 text-sm text-text-secondary break-words @2xl:max-w-xl">
          {{
            scopeAccountId === null
              ? t('backups.schedule.scope.hostWideHint')
              : t('backups.schedule.scope.overrideHint')
          }}
        </p>
        <UiSelect
          v-model="scope"
          class="w-full min-w-0 @2xl:w-80 @2xl:shrink-0"
          :label="t('backups.schedule.scope.label')"
          :options="scopeOptions"
          :disabled="store.loading || store.saving"
        />
      </div>
    </UiCard>

    <UiSpinner v-if="store.loading" :label="t('backups.schedule.loading')" />

    <!-- Form and side notes sit in two columns only from `@4xl` (56rem of the panel's width): the
         form's own two-up rows need ~32rem of their card, and 3/5 of anything narrower than this
         cannot give it. Below that the notes stack under the form. -->
    <div v-else-if="store.isLoaded" class="@container">
    <div class="grid items-start gap-4 @4xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
      <UiCard class="@container">
        <UiSectionHeading
          class="mb-4"
          :title="t('backups.schedule.form.heading')"
          :subtitle="t('backups.schedule.form.subtitle')"
        />
        <UiForm @submit="submit">
          <div class="flex flex-col gap-4">
            <div class="grid gap-4 @lg:grid-cols-2">
              <UiSelect
                v-model="frequency"
                class="min-w-0"
                :label="t('backups.schedule.form.frequencyLabel')"
                :placeholder="t('backups.schedule.form.frequencyPlaceholder')"
                :options="frequencyOptions"
                required
              />

              <UiSelect
                v-if="isWeekly"
                v-model="dayOfWeekUtc"
                class="min-w-0"
                :label="t('backups.schedule.form.dayLabel')"
                :placeholder="t('backups.schedule.form.dayPlaceholder')"
                :options="dayOptions"
                :error="fieldError('day')"
                required
              />
            </div>

            <div class="grid gap-4 @lg:grid-cols-2">
              <UiInput
                v-model="hourUtc"
                class="min-w-0"
                :label="t('backups.schedule.form.hourLabel')"
                :placeholder="t('backups.schedule.form.hourPlaceholder')"
                :error="fieldError('hour')"
                required
              />

              <UiInput
                v-model="retainCount"
                class="min-w-0"
                :label="t('backups.schedule.form.retainLabel')"
                :placeholder="t('backups.schedule.form.retainPlaceholder')"
                :error="fieldError('retain')"
                required
              />
            </div>

            <div class="flex flex-col gap-2 rounded-control border border-border-subtle bg-surface-2 p-4">
              <UiSwitch v-model="enabled" :label="t('backups.schedule.form.enabledLabel')" />
              <p class="text-sm text-text-muted break-words">{{ t('backups.schedule.form.enabledHint') }}</p>
            </div>

            <UiButton class="w-full @lg:w-auto @lg:self-start" type="submit" :disabled="store.saving">
              {{ store.saving ? t('common.saving') : t('backups.schedule.form.submit') }}
            </UiButton>
          </div>
        </UiForm>
      </UiCard>

      <div class="flex min-w-0 flex-col gap-4">
        <UiCard v-if="store.isConfigured">
          <UiSectionHeading class="mb-4" :title="t('backups.schedule.summary.heading')" />
          <UiDescriptionList>
            <UiDescriptionItem :term="t('backups.schedule.summary.state')">
              <UiBadge :variant="store.schedule?.enabled === true ? 'success' : 'warning'">
                {{
                  store.schedule?.enabled === true
                    ? t('backups.schedule.summary.on')
                    : t('backups.schedule.summary.off')
                }}
              </UiBadge>
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('backups.schedule.summary.lastRun')" mono>
              {{ lastRun }}
            </UiDescriptionItem>
          </UiDescriptionList>
        </UiCard>

        <UiCard>
          <UiSectionHeading class="mb-4" :title="t('backups.schedule.retention.heading')" />
          <!-- Every one of these is something `retainCount` on its own would be read as promising and
               does not. They are on the screen rather than in a doc comment because the operator who
               types a number is the person who would otherwise believe all four. -->
          <ul class="flex flex-col gap-4">
            <li v-for="point in retentionPoints" :key="point.key" class="flex items-start gap-3">
              <UiIconTile :name="point.icon" size="sm" />
              <p class="min-w-0 pt-1 text-sm text-text-secondary break-words">
                {{ point.text }}
              </p>
            </li>
          </ul>
        </UiCard>
      </div>
    </div>
    </div>
  </section>
</template>
