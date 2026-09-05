<script setup lang="ts">
/**
 * The form that installs a cron entry, or rewrites one: a schedule (builder or raw) and the command
 * line to run.
 *
 * **Nothing leaves this component until it would be accepted.** `submit` is emitted only once the
 * schedule and the command have both passed this panel's mirrors of `CronScheduleValidator` and
 * `CronCommandRule`; a refusal is stated here and no request is made. That is the whole point of
 * mirroring a server-side validator on the client (rules/vue.md) — the operator learns which half
 * of the form is wrong immediately, and the server, which validates it again and whose answer
 * decides, is not asked a question whose answer is already known.
 *
 * The form owns its fields and its validation; the page only forwards what it emits to the store.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import CronScheduleField from './CronScheduleField.vue'
import { isOneCronCommandLine } from '../../utils/cronCommand'
import type { CronSchedule } from '../../types/cronEntry'

/** Props accepted by {@link CronEntryForm}. */
const props = withDefaults(
  defineProps<{
    /** True while the panel is installing or rewriting, so the button says so and cannot be pressed twice. */
    submitting?: boolean
    /**
     * The entry being rewritten, or `null` to install a new one.
     *
     * Only the two things an edit may change travel here — the module's own update request carries
     * no enablement flag, because rewriting what a job runs and switching it back on are separate
     * decisions.
     */
    editing?: { entryId: string; schedule: CronSchedule; command: string } | null
  }>(),
  { submitting: false, editing: null },
)

/** Events emitted by {@link CronEntryForm}. */
const emit = defineEmits<{
  /**
   * The operator submitted a schedule and a command this panel would send.
   * @param e The event name.
   * @param value The schedule and the command, exactly as they will be sent.
   */
  (e: 'submit', value: { schedule: CronSchedule; command: string }): void

  /**
   * The operator abandoned an edit.
   * @param e The event name.
   */
  (e: 'cancel'): void
}>()

const { t } = useI18n()

/** The schedule the schedule field currently describes, or `null` when it would be refused. */
const schedule: Ref<CronSchedule | null> = ref(null)

/** The command line as typed. */
const command: Ref<string> = ref('')

/** The message under the command field, or `null`; set on submit rather than while typing. */
const commandError: Ref<string | null> = ref(null)

/** The message about the schedule, or `null`; set on submit. */
const scheduleError: Ref<string | null> = ref(null)

/** Whether the form is rewriting an entry rather than installing one. */
const isEditing: ComputedRef<boolean> = computed(() => {
  return props.editing !== null
})

/** The schedule the schedule field should start from — an edit's, or nothing. */
const initialSchedule: ComputedRef<CronSchedule | null> = computed(() => {
  return props.editing?.schedule ?? null
})

/**
 * Empties the form, so an accepted install does not leave the previous command in the field.
 * @returns Nothing.
 */
const reset = (): void => {
  command.value = ''
  commandError.value = null
  scheduleError.value = null
}

/**
 * Records the schedule the field is describing.
 * @param value The schedule, or null when the field would be refused.
 * @returns Nothing.
 */
const changeSchedule = (value: CronSchedule | null): void => {
  schedule.value = value
}

/**
 * Records the command as typed, clearing a refusal the operator is now acting on.
 * @param value The text now in the field.
 * @returns Nothing.
 */
const changeCommand = (value: string): void => {
  command.value = value
  commandError.value = null
}

/**
 * Abandons an edit.
 * @returns Nothing.
 */
const cancel = (): void => {
  reset()
  emit('cancel')
}

/**
 * Checks both halves and emits only when both would be accepted.
 *
 * Both are checked before either returns, so an operator with two mistakes is told about two
 * mistakes rather than being sent round the form twice.
 * @returns Nothing.
 */
const submit = (): void => {
  const currentSchedule = schedule.value
  scheduleError.value = currentSchedule === null ? t('cron.form.errors.schedule') : null
  commandError.value = isOneCronCommandLine(command.value) ? null : t('cron.form.errors.command')

  if (currentSchedule === null || commandError.value !== null) {
    return
  }

  emit('submit', { schedule: currentSchedule, command: command.value })
}

defineExpose({ reset })
</script>

<template>
  <UiForm @submit="submit">
    <CronScheduleField :schedule="initialSchedule" @update:model-value="changeSchedule" />

    <UiInput
      :model-value="command"
      :label="t('cron.form.fields.command')"
      :placeholder="t('cron.form.placeholders.command')"
      :error="commandError"
      class="mt-3"
      @update:model-value="changeCommand"
    />

    <p class="mt-1 text-sm text-text-muted">{{ t('cron.form.commandHint') }}</p>

    <p v-if="scheduleError !== null" class="mt-2 text-sm text-danger">{{ scheduleError }}</p>

    <div class="mt-3 flex items-center gap-2">
      <UiButton type="submit" :disabled="submitting">
        {{ submitting ? t('cron.form.working') : isEditing ? t('cron.form.save') : t('cron.form.create') }}
      </UiButton>
      <UiButton v-if="isEditing" variant="secondary" @click="cancel">
        {{ t('cron.form.cancel') }}
      </UiButton>
    </div>
  </UiForm>
</template>
