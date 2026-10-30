<script setup lang="ts">
/**
 * Moves one hosting account to another plan, from the account's own page.
 *
 * **The server decides whether the account fits.** A plan whose disk quota is below what the
 * account already holds is refused by the panel (`ChangeAccountPlanCommandHandler`) with a sentence
 * naming the remedy, and that sentence is shown here verbatim above the form; this dialog makes no
 * guess of its own about usage it cannot see. What it does check is the one thing it can know:
 * that a plan was chosen and that it is not the plan the account is already on.
 *
 * The plans are searched on the panel's picker choices (`GET /api/v1/plans/choices`), one page per
 * search and read fresh each time the picker opens, so every plan is reachable however many the
 * panel sells, a plan defined a moment ago is offered, and nothing is typed by hand (rules/vue.md).
 * The current plan is named from the account itself until another is chosen.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import UiForm from '../ui/UiForm.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { usePlanChoice } from '../../composables/usePlanChoice'
import { useAccountsStore } from '../../stores/accounts'
import type { AccountDetail } from '../../types/account'

/** Props accepted by {@link AccountPlanDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page. */
  open: boolean
  /** The account being moved. */
  account: AccountDetail
}>()

/** Events emitted by the dialog. */
const emit = defineEmits<{
  /**
   * Fired when the dialog should close without a change; the page owns the open state.
   * @param e The event name.
   */
  (e: 'close'): void
  /**
   * Fired once the panel has accepted the move.
   * @param e The event name.
   */
  (e: 'saved'): void
}>()

const { t } = useI18n()
const store = useAccountsStore()
const { picker: planPicker, search: searchPlans } = usePlanChoice()

/** The plan chosen in the select; set to the account's current plan each time the dialog opens. */
const planId: Ref<string> = ref('')

/** Whether the operator has tried to save, after which field errors are shown. */
const submitted: Ref<boolean> = ref(false)

/** Why the choice cannot be saved, or `null` when it can. */
const planError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (planId.value.length === 0) {
    return t('accounts.form.errors.planRequired')
  }
  return planId.value === props.account.planId ? t('accounts.edit.planUnchanged') : null
})

/**
 * Closes the dialog unless a request is in flight.
 * @returns Nothing.
 */
const close = (): void => {
  if (!store.editing) {
    emit('close')
  }
}

/**
 * Sends the move, and reports success to the page; a refusal stays on screen.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (planError.value !== null) {
    return
  }
  if (await store.changePlan(props.account.id, planId.value)) {
    emit('saved')
  }
}

// Every opening starts from the account's current plan and a clean slate; the picker reads its
// choices itself when opened.
watch(
  (): boolean => {
    return props.open
  },
  (open: boolean): void => {
    if (!open) {
      return
    }
    planId.value = props.account.planId
    submitted.value = false
    store.clearEditError()
  },
  { immediate: true },
)
</script>

<template>
  <UiModal
    :open="open"
    :title="t('accounts.edit.planTitle', { account: account.name })"
    :close-label="t('common.close')"
    :dismissible="!store.editing"
    @close="close"
  >
    <UiSpinner v-if="store.editing" :label="t('accounts.edit.saving')" />
    <UiForm v-else @submit="submit">
      <div class="flex flex-col gap-4">
        <UiAlert v-if="store.editErrorMessage !== null" variant="error">
          {{ store.editErrorMessage }}
        </UiAlert>
        <p class="text-sm text-text-muted">{{ t('accounts.edit.planHint') }}</p>
        <UiCombobox
          v-model="planId"
          v-bind="planPicker"
          :label="t('accounts.form.fields.plan')"
          :selected-label="account.planDisplayName"
          :error="planError"
          required
          :placeholder="t('accounts.form.placeholders.plan')"
          @search="searchPlans"
        />
      </div>
    </UiForm>

    <template #footer>
      <template v-if="!store.editing">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton @click="submit">{{ t('accounts.edit.planSubmit') }}</UiButton>
      </template>
    </template>
  </UiModal>
</template>
