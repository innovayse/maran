<script setup lang="ts">
/**
 * The editor for the assignments the agent manages in an account's crontab preamble.
 *
 * **It replaces the whole set, and says so.** The module's endpoint is a `PUT`, and the verb is the
 * warning: a name absent from what this sends is REMOVED from the crontab, and an empty set clears
 * them all. Assignments written outside the agent's own region are neither shown here nor touched.
 *
 * **The reserved-name hint is advice, never a decision (R13).** `MAILTO` and `SHELL` are refused by
 * `CronEnvironmentVariableValidator` and by the agent — one is an outbound relay through the host's
 * mail transfer agent, the other chooses the interpreter every one of that account's entries runs
 * under. This component marks them so an operator does not spend a round trip finding out, and then
 * lets the request go anyway: the refusal belongs to the server, and a client that enforced it
 * would be a second copy of an authorization rule with its own opportunity to be wrong. The hint is
 * therefore on the permissive side in both directions — it gates nothing, and it warns about
 * slightly more than the module refuses.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import { isReservedCronEnvironmentName } from '../../utils/cronEnvironmentName'
import type { CronEnvironmentVariable } from '../../types/cronEnvironmentVariable'

/** Props accepted by {@link CronEnvironmentEditor}. */
const props = defineProps<{
  /** The assignments the panel last reported. */
  variables: readonly CronEnvironmentVariable[]
  /** True while the set is being written, so the button says so and cannot be pressed twice. */
  saving: boolean
}>()

/** Events emitted by {@link CronEnvironmentEditor}. */
const emit = defineEmits<{
  /**
   * The operator asked for the complete set to be written.
   * @param e The event name.
   * @param value The complete new set; an empty list clears every managed assignment.
   */
  (e: 'save', value: CronEnvironmentVariable[]): void
}>()

const { t } = useI18n()

/**
 * The set being edited — a copy, so a half-finished edit is not written back over the panel's own
 * answer and an abandoned one is simply forgotten.
 */
const rows: Ref<CronEnvironmentVariable[]> = ref([])

/** The names in the set that this panel expects the module to refuse. */
const reservedNames: ComputedRef<string[]> = computed(() => {
  return rows.value
    .map((row) => {
      return row.name
    })
    .filter(isReservedCronEnvironmentName)
})

/**
 * Adds an empty row for a new assignment.
 * @returns Nothing.
 */
const addRow = (): void => {
  rows.value = [...rows.value, { name: '', value: '' }]
}

/**
 * Drops one row. Saving afterwards is what actually removes it from the crontab.
 * @param index The row to drop.
 * @returns Nothing.
 */
const removeRow = (index: number): void => {
  rows.value = rows.value.filter((_row, at) => {
    return at !== index
  })
}

/**
 * Records a row's new name.
 * @param index The row being edited.
 * @param name The name now in the field.
 * @returns Nothing.
 */
const changeName = (index: number, name: string): void => {
  rows.value = rows.value.map((row, at) => {
    return at === index ? { ...row, name } : row
  })
}

/**
 * Records a row's new value.
 * @param index The row being edited.
 * @param value The value now in the field.
 * @returns Nothing.
 */
const changeValue = (index: number, value: string): void => {
  rows.value = rows.value.map((row, at) => {
    return at === index ? { ...row, value } : row
  })
}

/**
 * Whether this row's name is one the agent writes itself.
 * @param name The name to check.
 * @returns True when the module is expected to refuse it.
 */
const isReserved = (name: string): boolean => {
  return isReservedCronEnvironmentName(name)
}

/**
 * Sends the complete set.
 *
 * Rows with no name at all are dropped rather than sent: an empty name is the shape of a row the
 * operator added and never filled in, and it is the one thing here that could not possibly be what
 * they meant. Everything else — including a reserved name — goes, and the server answers.
 * @returns Nothing.
 */
const save = (): void => {
  emit(
    'save',
    rows.value.filter((row) => {
      return row.name.length > 0
    }),
  )
}

// Seeded from the panel's answer whenever that answer changes — a fresh load, a different account,
// or the re-read that follows a successful write.
watch(
  () => {
    return props.variables
  },
  (current) => {
    rows.value = current.map((variable) => {
      return { ...variable }
    })
  },
  { immediate: true, deep: true },
)
</script>

<template>
  <UiForm @submit="save">
    <p class="mb-2 text-sm text-text-secondary">{{ t('cron.environment.description') }}</p>

    <div v-for="(row, index) in rows" :key="index" class="mb-3">
      <div class="flex flex-wrap items-end gap-2">
        <UiInput
          :model-value="row.name"
          :label="t('cron.environment.fields.name')"
          class="min-w-40 flex-1"
          @update:model-value="changeName(index, $event)"
        />
        <UiInput
          :model-value="row.value"
          :label="t('cron.environment.fields.value')"
          class="min-w-40 flex-1"
          @update:model-value="changeValue(index, $event)"
        />
        <UiButton
          variant="secondary"
          :aria-label="t('cron.environment.removeRow', { name: row.name })"
          @click="removeRow(index)"
        >
          {{ t('cron.environment.remove') }}
        </UiButton>
      </div>

      <!-- Advice, not a gate: the row stays submittable and the panel's own refusal is what the
           operator will see if they send it anyway. -->
      <p v-if="isReserved(row.name)" class="mt-1 text-sm text-warning">
        {{ t('cron.environment.reservedHint', { name: row.name }) }}
      </p>
    </div>

    <p v-if="reservedNames.length > 0" class="mb-2 text-sm text-text-muted">
      {{ t('cron.environment.reservedNote') }}
    </p>

    <div class="flex items-center gap-2">
      <UiButton variant="secondary" @click="addRow">{{ t('cron.environment.add') }}</UiButton>
      <UiButton type="submit" :disabled="saving">
        {{ saving ? t('cron.environment.working') : t('cron.environment.save') }}
      </UiButton>
    </div>

    <p class="mt-2 text-sm text-text-muted">{{ t('cron.environment.replaceWarning') }}</p>
  </UiForm>
</template>
