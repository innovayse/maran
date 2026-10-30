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
 * The plans are the panel's own list (`GET /api/v1/accounts/plans`), loaded when the dialog opens,
 * so a plan defined a moment ago is offered and nothing is typed by hand (rules/vue.md).
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiModal from '../ui/UiModal.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { useAccountsStore } from '../../stores/accounts'
import { formatMegabytes } from '../../utils/formatBytes'
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

/** The plan chosen in the select; set to the account's current plan each time the dialog opens. */
const planId: Ref<string> = ref('')

/** Whether the operator has tried to save, after which field errors are shown. */
const submitted: Ref<boolean> = ref(false)

/** The plans offered, each with its disk quota so the operator sees what the move changes. */
const planOptions: ComputedRef<SelectOption[]> = computed(() => {
  return store.plans.map((plan) => {
    return {
      value: plan.id,
      label: t(
        'accounts.form.planOption',
        { name: plan.displayName, disk: formatMegabytes(plan.diskQuotaMb), count: plan.maxSites },
        plan.maxSites,
      ),
    }
  })
})

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

// Every opening starts from the account's current plan and a clean slate, and fetches the list
// fresh so a plan defined since the page loaded is offered.
watch(
  (): boolean => {
    return props.open
  },
  async (open: boolean): Promise<void> => {
    if (!open) {
      return
    }
    planId.value = props.account.planId
    submitted.value = false
    store.clearEditError()
    await store.loadPlans()
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
        <UiSelect
          v-model="planId"
          :label="t('accounts.form.fields.plan')"
          :options="planOptions"
          :error="planError"
          required
          :placeholder="t('accounts.form.placeholders.plan')"
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
