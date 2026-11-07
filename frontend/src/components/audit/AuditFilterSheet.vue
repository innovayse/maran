<script setup lang="ts">
/**
 * The audit journal's filter sheet: action, outcome, the date window, the actor and the account.
 * The actor is a typed login name the panel matches exactly (it is a name, not a choice from a
 * list). The account is picked from the accounts matching the picker's search, the way the other
 * lists pick one; the journal attributes an entry to an account only where the panel could, so
 * the filter narrows to those entries. The actions and the outcomes offered are the ones the panel
 * lists, named by the panel. The window is picked as calendar days with `UiDateInput`; turning a
 * day into an instant is the API composable's job. Values go out on Apply exactly as
 * `UiFilterSheet` emits them; the page writes them to the URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCombobox from '../ui/UiCombobox.vue'
import UiDateInput from '../ui/UiDateInput.vue'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiInput from '../ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import type { AuditActionChoice, AuditOutcomeChoice } from '../../types/audit'

/** Props accepted by {@link AuditFilterSheet}. */
const props = defineProps<{
  /** Whether the sheet is shown. */
  open: boolean
  /** The applied filters by name (`action`, `outcome`, `from`, `to`, `actor`, `account`), empty when unset. */
  modelValue: Record<string, string>
  /** The actions the journal holds, as the panel named them. */
  actions: AuditActionChoice[]
  /** The outcomes the journal can be filtered by, as the panel named them. */
  outcomes: AuditOutcomeChoice[]
  /** The account picker's state: the page of accounts matching its search. */
  accountPicker: AccountPickerState
  /** The chosen account's name, as the page reads it; shown on the picker while the account is not on the page of matches. */
  accountName: string
}>()

/** Events emitted by {@link AuditFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the filters; carries every field. */
  (e: 'update:modelValue', value: Record<string, string>): void
  /** The reader dismissed or applied the sheet. */
  (e: 'close'): void
  /** The account picker asked for accounts matching a search. */
  (e: 'search', query: string): void
}>()

const { t } = useI18n()

/** The account picker after an "any account" choice, counted into the total. */
const accountFilterPicker: ComputedRef<AccountPickerState> = computed(() => {
  const state = props.accountPicker
  return {
    ...state,
    options: [{ value: '', label: t('common.list.any') }, ...state.options],
    total: state.total + 1,
  }
})

/** The actions the filter offers, after an "any action" choice. */
const actionOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...props.actions.map((choice) => {
      return { value: choice.action, label: choice.actionName }
    }),
  ]
})

/** The outcomes the filter offers, after an "any outcome" choice. */
const outcomeOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...props.outcomes.map((choice) => {
      return { value: choice.outcome, label: choice.outcomeName }
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
 * Passes an account search up.
 * @param query What the reader typed into the picker.
 * @returns Nothing.
 */
const search = (query: string): void => {
  emit('search', query)
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
      <UiSelect v-model="draft.action" :label="t('app.audit.filters.action')" :options="actionOptions" />
      <UiSelect v-model="draft.outcome" :label="t('app.audit.filters.outcome')" :options="outcomeOptions" />
      <UiDateInput v-model="draft.from" :label="t('app.audit.filters.from')" />
      <UiDateInput v-model="draft.to" :label="t('app.audit.filters.to')" />
      <UiInput
        v-model="draft.actor"
        :label="t('app.audit.filters.actor')"
        :placeholder="t('app.audit.filters.actorPlaceholder')"
        mono
      />
      <UiCombobox
        v-model="draft.account"
        v-bind="accountFilterPicker"
        :label="t('app.audit.filters.account')"
        :placeholder="t('common.list.any')"
        :selected-label="accountName"
        @search="search"
      />
    </template>
  </UiFilterSheet>
</template>
