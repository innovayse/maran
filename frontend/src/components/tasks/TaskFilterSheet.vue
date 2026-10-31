<script setup lang="ts">
/**
 * The tasks list's filter sheet: account (not offered to a customer, whose tasks are already their
 * own), status, operation, and the start-date window. The operations offered are the ones the panel
 * says the caller can see, named by the panel. The window is picked as calendar days with
 * `UiDateInput`; turning a day into an instant is the API composable's job. Values go out on Apply
 * exactly as `UiFilterSheet` emits them; the page writes them to the URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCombobox from '../ui/UiCombobox.vue'
import UiDateInput from '../ui/UiDateInput.vue'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import { TASK_STATUSES, type TaskKindChoice } from '../../types/panelTask'

/** Props accepted by {@link TaskFilterSheet}. */
const props = defineProps<{
  /** Whether the sheet is shown. */
  open: boolean
  /** The applied filters by name (`account`, `status`, `kind`, `from`, `to`), empty when unset. */
  modelValue: Record<string, string>
  /** Whether the account field is offered; false for a customer. */
  showAccount: boolean
  /** The account choices matching the field's search. */
  accountPicker: AccountPickerState
  /** The name of the account the list is narrowed to, for the field's closed state. */
  accountName: string
  /** The kinds of task the caller can see, as the panel named them. */
  kinds: TaskKindChoice[]
}>()

/** Events emitted by {@link TaskFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the filters; carries every field. */
  (e: 'update:modelValue', value: Record<string, string>): void
  /** The reader dismissed or applied the sheet. */
  (e: 'close'): void
  /** The account field's search changed. */
  (e: 'searchAccounts', term: string): void
}>()

const { t } = useI18n()

/** The account choices after an "any account" choice, counted into the total so "more matches" stays right. */
const accountChoices: ComputedRef<AccountPickerState> = computed(() => {
  const state = props.accountPicker
  return {
    ...state,
    options: [{ value: '', label: t('common.list.any') }, ...state.options],
    total: state.total + 1,
  }
})

/** The statuses the filter offers, after an "any status" choice. */
const statusOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...TASK_STATUSES.map((status) => {
      return { value: status, label: t(`tasks.statuses.${status}`) }
    }),
  ]
})

/** The operations the filter offers, after an "any operation" choice. */
const kindOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...props.kinds.map((choice) => {
      return { value: choice.kind, label: choice.kindName }
    }),
  ]
})

/**
 * Passes the applied filters up.
 * @param value Every field of the sheet.
 * @returns Nothing.
 */
const apply = (value: Record<string, string>): void => {
  emit('update:modelValue', value)
}

/**
 * Passes the dismissal up.
 * @returns Nothing.
 */
const close = (): void => {
  emit('close')
}

/**
 * Passes the account field's search up.
 * @param term The typed text.
 * @returns Nothing.
 */
const searchAccounts = (term: string): void => {
  emit('searchAccounts', term)
}
</script>

<template>
  <UiFilterSheet
    :open="open"
    :model-value="modelValue"
    :title="t('common.list.filters')"
    :close-label="t('common.close')"
    :apply-label="t('common.list.apply')"
    :reset-label="t('common.list.reset')"
    @update:model-value="apply"
    @close="close"
  >
    <template #default="{ draft }">
      <UiCombobox
        v-if="showAccount"
        v-model="draft.account"
        v-bind="accountChoices"
        :label="t('tasks.list.filters.account')"
        :placeholder="t('common.list.any')"
        :selected-label="accountName"
        @search="searchAccounts"
      />
      <UiSelect v-model="draft.status" :label="t('tasks.list.filters.status')" :options="statusOptions" />
      <UiSelect v-model="draft.kind" :label="t('tasks.list.filters.kind')" :options="kindOptions" />
      <UiDateInput v-model="draft.from" :label="t('tasks.list.filters.from')" />
      <UiDateInput v-model="draft.to" :label="t('tasks.list.filters.to')" />
    </template>
  </UiFilterSheet>
</template>
