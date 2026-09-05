<script setup lang="ts">
/**
 * The schedule half of the cron entry form, in the two modes an operator actually works in.
 *
 * **Builder mode** offers a frequency and the parts that frequency uses, and maps them onto the
 * module's five fields. It deliberately cannot express lists, ranges and steps: at that point the
 * operator is writing cron, and a form is in their way.
 *
 * **Raw mode** takes the whole expression — the thing that was already in their clipboard — and
 * splits it into the five fields the contract carries. It is the escape hatch that keeps the
 * builder from having to grow.
 *
 * The component emits the schedule, or `null` when this panel would not send it. `null` is a
 * refusal made HERE, before any request: `utils/cronSchedule.ts` mirrors the module's own
 * `CronScheduleValidator`, so an expression that cannot be a schedule is named as such instead of
 * being posted for the server to say the same thing a round trip later. The server still decides —
 * whatever gets through is validated there and in the agent again.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiInput from '../ui/UiInput.vue'
import UiSegmentedControl, { type SegmentOption } from '../ui/UiSegmentedControl.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import {
  buildCronSchedule,
  formatCronExpression,
  isValidCronSchedule,
  parseCronExpression,
} from '../../utils/cronSchedule'
import type { CronSchedule, CronScheduleFrequency } from '../../types/cronEntry'

/** Which way the operator is currently writing the schedule. */
type ScheduleMode = 'builder' | 'raw'

/** Props accepted by {@link CronScheduleField}. */
const props = defineProps<{
  /**
   * The schedule to start from, or `null` to start on the builder's default.
   *
   * Read once, when it changes identity — an edit reopening the form on a different entry — rather
   * than continuously, so the operator's typing is never overwritten by the value they are editing.
   */
  schedule: CronSchedule | null
}>()

/** Events emitted by {@link CronScheduleField}. */
const emit = defineEmits<{
  /**
   * The schedule currently described, or `null` when this panel would refuse to send it.
   * @param e The event name.
   * @param value The schedule, or null when the current text is not one.
   */
  (e: 'update:modelValue', value: CronSchedule | null): void
}>()

/** The frequencies the builder offers, in the order they read. */
const FREQUENCIES: readonly CronScheduleFrequency[] = [
  'everyMinute',
  'hourly',
  'daily',
  'weekly',
  'monthly',
]

/**
 * The days of the week, as cron numbers them alongside the key each is named by.
 *
 * The number is the contract — cron counts Sunday as 0 — and the name is this panel's own chrome.
 * They are paired here rather than the locale being keyed by the digit, because a numeric segment
 * in an i18n path reads as an array index rather than a key.
 */
const WEEKDAYS: readonly { readonly value: string; readonly key: string }[] = [
  { value: '0', key: 'sunday' },
  { value: '1', key: 'monday' },
  { value: '2', key: 'tuesday' },
  { value: '3', key: 'wednesday' },
  { value: '4', key: 'thursday' },
  { value: '5', key: 'friday' },
  { value: '6', key: 'saturday' },
]

const { t } = useI18n()

/** Which mode the operator is in. Builder first: it is the one that cannot be got wrong. */
const mode: Ref<ScheduleMode> = ref('builder')

/** The builder's frequency. */
const frequency: Ref<CronScheduleFrequency> = ref('daily')

/** The builder's minute of the hour. */
const minute: Ref<string> = ref('0')

/** The builder's hour of the day. */
const hour: Ref<string> = ref('3')

/** The builder's day of the week, cron-numbered. */
const dayOfWeek: Ref<string> = ref('1')

/** The builder's day of the month. */
const dayOfMonth: Ref<string> = ref('1')

/** The whole expression as typed in raw mode. */
const expression: Ref<string> = ref('')

/** Whether the operator has typed in raw mode yet, so an untouched field is not marked wrong. */
const rawTouched: Ref<boolean> = ref(false)

/** The two modes, as the segmented control wants them. */
const modeOptions: ComputedRef<SegmentOption[]> = computed(() => {
  return [
    { value: 'builder', label: t('cron.schedule.modes.builder') },
    { value: 'raw', label: t('cron.schedule.modes.raw') },
  ]
})

/** The frequency choices, already translated. */
const frequencyOptions: ComputedRef<SelectOption[]> = computed(() => {
  return FREQUENCIES.map((value) => {
    return { value, label: t(`cron.schedule.frequencies.${value}`) }
  })
})

/** The weekday choices, already translated; the value is the number cron uses. */
const weekdayOptions: ComputedRef<SelectOption[]> = computed(() => {
  return WEEKDAYS.map((day) => {
    return { value: day.value, label: t(`cron.schedule.weekdays.${day.key}`) }
  })
})

/** Whether the builder's frequency uses a minute of the hour. */
const usesMinute: ComputedRef<boolean> = computed(() => {
  return frequency.value !== 'everyMinute'
})

/** Whether the builder's frequency uses an hour of the day. */
const usesHour: ComputedRef<boolean> = computed(() => {
  return frequency.value === 'daily' || frequency.value === 'weekly' || frequency.value === 'monthly'
})

/**
 * The schedule the current inputs describe, or `null` when this panel would refuse it.
 *
 * Raw mode refuses in two distinct ways and both matter: an expression that is not exactly five
 * whitespace-separated fields parses to `null`, and one that parses but breaks the module's grammar
 * fails the mirror. Either way nothing is sent.
 */
const currentSchedule: ComputedRef<CronSchedule | null> = computed(() => {
  const candidate =
    mode.value === 'raw'
      ? parseCronExpression(expression.value)
      : buildCronSchedule({
          frequency: frequency.value,
          minute: minute.value,
          hour: hour.value,
          dayOfWeek: dayOfWeek.value,
          dayOfMonth: dayOfMonth.value,
        })

  return candidate !== null && isValidCronSchedule(candidate) ? candidate : null
})

/** The crontab line the current inputs describe, shown so the two modes agree in front of the operator. */
const preview: ComputedRef<string> = computed(() => {
  const current = currentSchedule.value
  return current === null ? t('cron.schedule.previewUnavailable') : formatCronExpression(current)
})

/** The message shown under the raw field, or `null` while it is acceptable or untouched. */
const rawError: ComputedRef<string | null> = computed(() => {
  if (mode.value !== 'raw' || !rawTouched.value || currentSchedule.value !== null) {
    return null
  }
  return t('cron.schedule.errors.expression')
})

/** The message shown under the builder, or `null` while its values are acceptable. */
const builderError: ComputedRef<string | null> = computed(() => {
  if (mode.value !== 'builder' || currentSchedule.value !== null) {
    return null
  }
  return t('cron.schedule.errors.builder')
})

/**
 * Chooses the builder's frequency.
 *
 * A named handler rather than a cast in the template: the select's contract is a plain string, and
 * narrowing it here means the one place that decides a string is a frequency is readable TypeScript
 * rather than an assertion buried in markup.
 * @param value The frequency chosen, as the select reports it.
 * @returns Nothing.
 */
const changeFrequency = (value: string): void => {
  const chosen = FREQUENCIES.find((candidate) => {
    return candidate === value
  })
  if (chosen !== undefined) {
    frequency.value = chosen
  }
}

/**
 * Switches mode, carrying the current schedule across so nothing is retyped.
 *
 * Going to raw seeds the field with the expression the builder was describing; going back leaves
 * the builder exactly as it was, because a raw expression generally has no builder that means it
 * and guessing one would change the schedule under the operator.
 * @param next The mode chosen.
 * @returns Nothing.
 */
const changeMode = (next: string): void => {
  if (next === 'raw' && mode.value === 'builder') {
    const current = currentSchedule.value
    expression.value = current === null ? expression.value : formatCronExpression(current)
    rawTouched.value = expression.value.length > 0
  }
  mode.value = next === 'raw' ? 'raw' : 'builder'
}

/**
 * Records that the operator has typed a raw expression, so the field may now be marked wrong.
 * @param value The text now in the field.
 * @returns Nothing.
 */
const changeExpression = (value: string): void => {
  expression.value = value
  rawTouched.value = true
}

// Emitted rather than exposed as a computed prop, so the parent form holds one value and the
// refusal travels with it: a parent that received only the builder's parts would have to re-derive
// the schedule, and the two derivations would be free to disagree.
watch(
  currentSchedule,
  (current) => {
    emit('update:modelValue', current)
  },
  { immediate: true },
)

// An entry being edited seeds RAW mode, never the builder: most real schedules cannot be expressed
// by the builder at all, and quietly rounding one to the nearest thing the builder can say would
// rewrite a job the operator only opened to look at.
watch(
  () => {
    return props.schedule
  },
  (current) => {
    if (current === null) {
      return
    }
    expression.value = formatCronExpression(current)
    rawTouched.value = true
    mode.value = 'raw'
  },
  { immediate: true },
)
</script>

<template>
  <fieldset class="rounded-lg border border-border-subtle p-3">
    <legend class="px-1 text-xs uppercase tracking-wide text-text-muted">
      {{ t('cron.schedule.legend') }}
    </legend>

    <UiSegmentedControl
      :model-value="mode"
      :options="modeOptions"
      :label="t('cron.schedule.modeLabel')"
      class="mb-3"
      @update:model-value="changeMode"
    />

    <div v-if="mode === 'builder'" class="grid gap-3 sm:grid-cols-2">
      <UiSelect
        :model-value="frequency"
        :options="frequencyOptions"
        :label="t('cron.schedule.fields.frequency')"
        @update:model-value="changeFrequency"
      />
      <UiInput
        v-if="usesMinute"
        :model-value="minute"
        :label="t('cron.schedule.fields.minute')"
        @update:model-value="minute = $event"
      />
      <UiInput
        v-if="usesHour"
        :model-value="hour"
        :label="t('cron.schedule.fields.hour')"
        @update:model-value="hour = $event"
      />
      <UiSelect
        v-if="frequency === 'weekly'"
        :model-value="dayOfWeek"
        :options="weekdayOptions"
        :label="t('cron.schedule.fields.dayOfWeek')"
        @update:model-value="dayOfWeek = $event"
      />
      <UiInput
        v-if="frequency === 'monthly'"
        :model-value="dayOfMonth"
        :label="t('cron.schedule.fields.dayOfMonth')"
        @update:model-value="dayOfMonth = $event"
      />
    </div>

    <UiInput
      v-else
      :model-value="expression"
      :label="t('cron.schedule.fields.expression')"
      :placeholder="t('cron.schedule.placeholders.expression')"
      :error="rawError"
      @update:model-value="changeExpression"
    />

    <p v-if="builderError !== null" class="mt-2 text-sm text-danger">{{ builderError }}</p>

    <!-- The five fields as the module will receive them. Both modes end here, which is what lets an
         operator check a builder pattern against the cron they already know. -->
    <p class="mt-3 text-sm text-text-secondary">
      {{ t('cron.schedule.previewLabel') }}
      <span class="font-mono text-text-primary" data-testid="cron-schedule-preview">{{ preview }}</span>
    </p>
  </fieldset>
</template>
