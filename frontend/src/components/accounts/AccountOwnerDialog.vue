<script setup lang="ts">
/**
 * Hands one hosting account to another owner, from the account's own page: the administrator, or a
 * reseller, together with a plan of that owner. Administrator only; the page draws the opening
 * action for an administrator alone and the endpoint refuses everyone else.
 *
 * **Two steps.** The form chooses the owner and the plan; "Continue" then asks a confirming question
 * that names the account, the old and the new owner and the plan, and says what the move changes
 * for the account's former reseller. Only the confirmation sends the request. A move is not trivial
 * to undo: it reshapes two resellers' allocations and ends the view-as sessions the former reseller
 * had on the account.
 *
 * **The server decides everything that is not a missing choice.** An owner that already owns the
 * account, a new owner with no room left in its envelope, a plan of another owner, an allocation
 * that changed under the move: each is refused by the panel with its own localized sentence, shown
 * here verbatim above the form (rules/vue.md), and the dialog stays open on the choices made. This
 * dialog checks only that a plan was chosen.
 *
 * **The plan belongs to the owner.** The plans offered are those of the owner chosen so far
 * (`GET /api/v1/plans/choices?forResellerId=`), so changing the owner clears the plan: a plan of the
 * previous choice would be refused as belonging to someone else. Owners come from
 * `GET /api/v1/resellers/choices`, searched on the server, with the administrator as the first
 * entry; nothing is typed by hand.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { usePlanChoice } from '../../composables/usePlanChoice'
import { useResellerChoice } from '../../composables/useResellerChoice'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import { useAccountsStore } from '../../stores/accounts'
import type { AccountDetail } from '../../types/account'

/**
 * The owner picker's value for "the administrator". A reseller's value is its id, a GUID, so this
 * can never collide with one; it exists because the picker's empty string means "nothing chosen".
 */
const ADMINISTRATOR = 'administrator'

/** Props accepted by {@link AccountOwnerDialog}. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page. */
  open: boolean
  /** The account being handed over. */
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
const { picker: resellerPicker, search: searchResellers } = useResellerChoice()

/** The owner chosen so far: a reseller's id, or {@link ADMINISTRATOR}; set to the current owner on each opening. */
const owner: Ref<string> = ref(ADMINISTRATOR)

/** The plan chosen, a plan of {@link owner}; empty until one is chosen. */
const planId: Ref<string> = ref('')

/** Whether the operator has tried to continue, after which field errors are shown. */
const submitted: Ref<boolean> = ref(false)

/** Whether the confirming question is showing instead of the form. */
const confirming: Ref<boolean> = ref(false)

/** The reseller id the request names: `null` when the administrator is chosen. */
const resellerId: ComputedRef<string | null> = computed(() => {
  return owner.value === ADMINISTRATOR ? null : owner.value
})

const { picker: planPicker, search: searchPlans } = usePlanChoice((): string | null => {
  return resellerId.value
})

/** The owner picker: the administrator first, then the page of resellers matching the search. */
const ownerPicker: ComputedRef<AccountPickerState> = computed(() => {
  const state = resellerPicker.value
  return {
    ...state,
    options: [{ value: ADMINISTRATOR, label: t('accounts.form.noReseller') }, ...state.options],
    total: state.total + 1,
  }
})

/** How the account's current owner is named: the reseller's name, or the administrator. */
const currentOwnerName: ComputedRef<string> = computed(() => {
  return props.account.resellerName ?? t('accounts.form.noReseller')
})

/** The label of the owner chosen, from the page of choices, or the current owner's name while it is unchanged. */
const chosenOwnerName: ComputedRef<string> = computed(() => {
  if (owner.value === ADMINISTRATOR) {
    return t('accounts.form.noReseller')
  }
  const found = ownerPicker.value.options.find((option) => {
    return option.value === owner.value
  })
  return found?.label ?? currentOwnerName.value
})

/** The label of the plan chosen, from the open page of plans. */
const chosenPlanName: ComputedRef<string> = computed(() => {
  const found = planPicker.value.options.find((option) => {
    return option.value === planId.value
  })
  return found?.label ?? ''
})

/** Why the choice cannot be continued, or `null` when it can. */
const planError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return planId.value.length === 0 ? t('accounts.edit.ownerPlanRequired') : null
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
 * Moves from the form to the confirming question, once a plan is chosen.
 * @returns Nothing.
 */
const proceed = (): void => {
  submitted.value = true
  if (planError.value !== null) {
    return
  }
  store.clearEditError()
  confirming.value = true
}

/**
 * Returns from the confirming question to the form, keeping the choices.
 * @returns Nothing.
 */
const back = (): void => {
  confirming.value = false
}

/**
 * Sends the move, and reports success to the page. A refusal returns to the form, over the choices
 * made, with the panel's sentence above them.
 * @returns Resolves once the request has settled.
 */
const send = async (): Promise<void> => {
  if (await store.transfer(props.account.id, resellerId.value, planId.value)) {
    emit('saved')
    return
  }
  confirming.value = false
}

/**
 * Takes a new owner and drops the plan chosen for the previous one, which the new owner cannot sell.
 * @param value The owner chosen in the picker.
 * @returns Nothing.
 */
const chooseOwner = (value: string): void => {
  if (value !== owner.value) {
    planId.value = ''
  }
  owner.value = value
}

// Every opening starts from the account's current owner, no plan and a clean slate; the pickers
// read their choices themselves when opened.
watch(
  (): boolean => {
    return props.open
  },
  (open: boolean): void => {
    if (!open) {
      return
    }
    owner.value = props.account.resellerId ?? ADMINISTRATOR
    planId.value = ''
    submitted.value = false
    confirming.value = false
    store.clearEditError()
  },
  { immediate: true },
)
</script>

<template>
  <UiModal
    :open="open"
    :title="t('accounts.edit.ownerTitle', { account: account.name })"
    :close-label="t('common.close')"
    :dismissible="!store.editing"
    @close="close"
  >
    <UiSpinner v-if="store.editing" :label="t('accounts.edit.saving')" />
    <div v-else-if="confirming" class="flex flex-col gap-3">
      <p class="text-sm">
        {{
          t('accounts.edit.ownerConfirm', {
            account: account.name,
            from: currentOwnerName,
            to: chosenOwnerName,
            plan: chosenPlanName,
          })
        }}
      </p>
      <p class="text-sm text-text-muted">{{ t('accounts.edit.ownerConfirmConsequence') }}</p>
    </div>
    <UiForm v-else @submit="proceed">
      <div class="flex flex-col gap-4">
        <UiAlert v-if="store.editErrorMessage !== null" variant="error">
          {{ store.editErrorMessage }}
        </UiAlert>
        <p class="text-sm text-text-muted">{{ t('accounts.edit.ownerHint') }}</p>
        <UiCombobox
          :model-value="owner"
          v-bind="ownerPicker"
          :label="t('accounts.edit.ownerField')"
          :selected-label="currentOwnerName"
          required
          :placeholder="t('accounts.form.noReseller')"
          @update:model-value="chooseOwner"
          @search="searchResellers"
        />
        <div class="flex flex-col gap-1">
          <UiCombobox
            v-model="planId"
            v-bind="planPicker"
            :label="t('accounts.edit.ownerPlanField')"
            :error="planError"
            required
            :placeholder="t('accounts.form.placeholders.plan')"
            @search="searchPlans"
          />
          <UiFieldHint>{{ t('accounts.edit.ownerPlanHint') }}</UiFieldHint>
        </div>
      </div>
    </UiForm>

    <template #footer>
      <template v-if="!store.editing && confirming">
        <UiButton variant="secondary" @click="back">{{ t('accounts.edit.ownerBack') }}</UiButton>
        <UiButton @click="send">{{ t('accounts.edit.ownerSubmit') }}</UiButton>
      </template>
      <template v-else-if="!store.editing">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton @click="proceed">{{ t('accounts.edit.ownerContinue') }}</UiButton>
      </template>
    </template>
  </UiModal>
</template>
