<script setup lang="ts">
/**
 * One hosting account, with the three things that can be done to it: suspend,
 * reactivate, delete. Renders a `<section>`, not a `<main>` — the single `<main>`
 * landmark lives in the layout this page is nested under.
 *
 * Every action asks first. Two of them are visible to the account's customer
 * within seconds (their sites stop serving); none should be one stray click
 * away. The confirmation names what will happen rather than asking "are you
 * sure", because the operator is being asked to weigh a consequence, not to
 * repeat themselves.
 *
 * **Suspension's confirmation names the transfer it interrupts.** Suspending now
 * ends the file-transfer sessions the account already has open, as well as
 * refusing new ones, so a transfer in flight is cut and the account's home is
 * left holding a file shorter than it should be. Suspension itself is reversible
 * by the control beside it and deletes nothing; that cut transfer is the one
 * thing a resume does not give back, so it is the one thing the question adds.
 *
 * The two reversible actions ask in {@link UiConfirm} — a modal that names the
 * account, states the consequence and takes one answer. It replaced a sentence
 * spliced into the row of buttons, which the operator could miss entirely on a
 * page they had scrolled past.
 *
 * **Deletion is asked differently.** Suspension and reactivation are reversible
 * by the control beside them, so a yes/no question is the right weight for them.
 * Deleting removes the system user, the home directory and every database — and
 * it is confirmed by typing the account's name in {@link AccountDeleteDialog},
 * which is also the only place the final backup taken before the cascade is
 * described.
 *
 * **"Resend invitation" is the recovery path for a login the owner never reached** —
 * a panel with no SMTP configured when the account was created, or an
 * `AccountCreated` handler that failed after the account row already existed.
 * Administrator-only on the server (`InvitationsController`); this page adds no
 * matching role check of its own (rules/architecture.md — the endpoint is the
 * boundary). Its confirmation says the invitation was SENT, never delivered:
 * the panel's mail queue is deliberately non-durable and cannot confirm receipt.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import AccountDeleteDialog from '../../components/accounts/AccountDeleteDialog.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The account's identity, from `/accounts/:id`. */
  id: string
}>()

/** The lifecycle action awaiting a yes/no answer, or `null` when none is. */
type PendingAction = 'suspend' | 'reactivate' | null

const { t } = useI18n()
const router = useRouter()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()

/** Which action the operator has started and is being asked to confirm. */
const pending: Ref<PendingAction> = ref(null)

/** Whether the deletion dialog is open. */
const deleting: Ref<boolean> = ref(false)

/**
 * True once this page has sent an invitation and the panel accepted it. Cleared
 * whenever a fresh resend is attempted, so a second click cannot leave a stale
 * confirmation on screen for a request that has not answered yet.
 */
const invitationResent: Ref<boolean> = ref(false)

/** The confirmation's title, naming the account and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const account = accountsStore.selected?.name ?? ''
  return pending.value === 'suspend'
    ? t('accounts.detail.confirmSuspendTitle', { account })
    : t('accounts.detail.confirmReactivateTitle', { account })
})

/** The consequence the confirmation asks the operator to weigh. */
const confirmationText: ComputedRef<string> = computed(() => {
  return pending.value === 'suspend'
    ? t('accounts.detail.confirmSuspend')
    : t('accounts.detail.confirmReactivate')
})

/**
 * Starts an action, which then waits for confirmation.
 * @param action The action the operator clicked.
 * @returns Nothing; the page switches to its confirming state.
 */
const ask = (action: Exclude<PendingAction, null>): void => {
  pending.value = action
}

/**
 * Abandons a pending action.
 * @returns Nothing; the page returns to its normal state.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed action.
 *
 * The dialog is closed AFTER the request settles, not before it is sent: it is
 * what shows the operator that something is happening, and closing it first
 * would put the wait on a page that says nothing about it.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const action = pending.value

  if (action === 'suspend') {
    await accountsStore.suspend(props.id)
  } else if (action === 'reactivate') {
    await accountsStore.reactivate(props.id)
  }

  pending.value = null
}

/**
 * Opens the deletion dialog, clearing any confirmation still on screen so two
 * questions are never being asked at once.
 * @returns Nothing.
 */
const askDelete = (): void => {
  pending.value = null
  deleting.value = true
}

/**
 * Closes the deletion dialog without deleting anything.
 * @returns Nothing.
 */
const cancelDelete = (): void => {
  deleting.value = false
}

/**
 * Resends the invitation for this account's owner.
 *
 * Reports only that the panel SENT it: the mail queue is deliberately
 * non-durable, so nothing here can say the mail arrived.
 * @returns Resolves once the request has settled.
 */
const resendInvitation = async (): Promise<void> => {
  invitationResent.value = false
  invitationResent.value = await accountsStore.resendInvitation(props.id)
}

/**
 * Deletes the account the dialog was confirmed for, and leaves for the list once
 * it is gone — the page it was on no longer describes anything. A refusal keeps
 * the dialog open over the message the panel sent.
 *
 * The request is made HERE rather than inside the dialog because a successful
 * deletion clears the store's `selected`, which unmounts the branch the dialog
 * is rendered in before an `await` inside it would resume.
 * @returns Resolves once the request has settled and any navigation is done.
 */
const remove = async (): Promise<void> => {
  if (!(await accountsStore.remove(props.id))) {
    return
  }

  deleting.value = false
  await router.push({ name: 'accounts' })
}

onMounted(async () => {
  await accountsStore.loadOne(props.id)
})
</script>

<template>
  <section class="w-full">
    <UiSpinner v-if="accountsStore.loading" :label="t('accounts.detail.loading')" />

    <template v-else-if="accountsStore.selected !== null">
      <UiPageHeading
        class="mb-4"
        :title="accountsStore.selected.name"
        :subtitle="accountsStore.selected.primaryDomain"
      >
        <template #actions>
          <UiButton variant="ghost" @click="router.push({ name: 'accounts' })">
            {{ t('accounts.detail.backToList') }}
          </UiButton>
        </template>
      </UiPageHeading>

      <UiAlert v-if="accountsStore.errorMessage !== null" variant="error" class="mb-4">
        {{ accountsStore.errorMessage }}
      </UiAlert>

      <!-- Says SENT, never delivered: the mail queue is deliberately non-durable and this page
           has no way to know whether the mail arrived. -->
      <UiAlert v-if="invitationResent" variant="info" class="mb-4">
        {{ t('accounts.detail.invitationResent') }}
      </UiAlert>

      <UiCard>
        <UiDescriptionList>
          <UiDescriptionItem :term="t('common.name')">
            {{ accountsStore.selected.name }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('accounts.detail.primaryDomainLabel')" mono>
            {{ accountsStore.selected.primaryDomain }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('accounts.detail.statusLabel')">
            <UiBadge :variant="accountsStore.selected.status === 'active' ? 'success' : 'warning'">
              {{ t(`accounts.status.${accountsStore.selected.status}`) }}
            </UiBadge>
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('accounts.detail.createdAtLabel')">
            {{ formatDate(accountsStore.selected.createdAt, localeStore.current) }}
          </UiDescriptionItem>
        </UiDescriptionList>
      </UiCard>

      <div class="mt-4 flex flex-wrap items-center gap-2">
        <UiButton
          v-if="accountsStore.selected.status === 'active'"
          variant="secondary"
          @click="ask('suspend')"
        >
          {{ t('accounts.detail.suspend') }}
        </UiButton>
        <UiButton v-else variant="secondary" @click="ask('reactivate')">
          {{ t('accounts.detail.reactivate') }}
        </UiButton>
        <UiButton variant="destructive" @click="askDelete">
          {{ t('common.delete') }}
        </UiButton>
        <!-- Administrator-only on the server; no matching check here (rules/architecture.md). The
             recovery path for an owner who never received their first invitation. -->
        <UiButton variant="secondary" :disabled="accountsStore.acting" @click="resendInvitation">
          {{ t('accounts.detail.resendInvitation') }}
        </UiButton>
      </div>

      <!-- `:open` is bound to a value that really changes, and this dialog needs no `v-if`: it has
           no props that are only valid while it is open, and `UiModal` renders nothing while
           closed. The deletion dialog below cannot say the same — its `account` prop is
           `accountsStore.selected`, which is nullable — so it is mounted with `v-if` at the moment
           it opens, and `UiModal`s open-watcher is `immediate` so that case works too. -->
      <UiConfirm
        :open="pending !== null"
        :title="confirmationTitle"
        :question="confirmationText"
        :confirm-label="t('common.confirm')"
        :cancel-label="t('common.cancel')"
        :close-label="t('common.close')"
        :acting="accountsStore.acting"
        :acting-label="t('accounts.detail.working')"
        :destructive="pending === 'suspend'"
        @close="cancel"
        @confirm="confirm"
      />

      <!-- `v-if` because `account` is required and `accountsStore.selected` is nullable; the
           dialog is created with `open` already true, which `UiModal`s immediate open-watcher
           handles (focus enters, Escape reaches it, focus returns to the button that opened it). -->
      <AccountDeleteDialog
        v-if="deleting"
        :open="deleting"
        :account="accountsStore.selected"
        @close="cancelDelete"
        @confirmed="remove"
      />
    </template>

    <UiAlert v-else-if="accountsStore.errorMessage !== null" variant="error">
      {{ accountsStore.errorMessage }}
    </UiAlert>

    <UiEmptyState
      v-else
      :title="t('accounts.detail.notFoundTitle')"
      :description="t('accounts.detail.notFoundDescription')"
    />
  </section>
</template>
