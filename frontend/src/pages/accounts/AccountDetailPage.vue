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
 * **The owner is handed over in {@link AccountOwnerDialog}** (#369), offered to an administrator
 * only: the move of an account to a reseller, or back to the administrator, with a plan of the new
 * owner. The endpoint is administrator-only, so the action is advice for the screen.
 *
 * **The plan and the owner's address are edited in place** ({@link AccountPlanDialog},
 * {@link AccountOwnerEmailDialog}, #216). Both are refused by the panel when they cannot be applied
 * — a plan the account's disk usage does not fit, an address another login holds — and the dialog
 * shows that sentence; this page only says, on success, what changed.
 *
 * **The owner's invitation lives in {@link AccountInvitationCard}**, below the details (#78): where
 * it stands, a one-time link to hand over by hand (the way in on a panel that cannot send mail),
 * and "Resend invitation" as the recovery path for a login the owner never reached. All of it is
 * administrator-only on the server (`InvitationsController`), so the card is drawn for an administrator
 * only: a reseller opening its own account would be shown a refusal it cannot act on. That is
 * advice for the screen; the endpoint is the boundary (rules/architecture.md).
 *
 * **While the account loads**, the heading and the details card are drawn already, with their real
 * terms and a placeholder line in place of each value ({@link UiLoadingRegion},
 * {@link AccountDetailsCard}), so the account lands in the space held for it. The resources card
 * reads its own summary and says on its own how that read stands.
 *
 * **The personal-data export** ({@link AccountPersonalDataExport}, #146) sits under the invitation
 * card and, like it, is drawn for an administrator only: the endpoint refuses everyone else.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import AccountActionsCard from '../../components/accounts/AccountActionsCard.vue'
import AccountDeleteDialog from '../../components/accounts/AccountDeleteDialog.vue'
import AccountDetailsCard from '../../components/accounts/AccountDetailsCard.vue'
import AccountInvitationCard from '../../components/accounts/AccountInvitationCard.vue'
import AccountOwnerDialog from '../../components/accounts/AccountOwnerDialog.vue'
import AccountOwnerEmailDialog from '../../components/accounts/AccountOwnerEmailDialog.vue'
import AccountPersonalDataExport from '../../components/accounts/AccountPersonalDataExport.vue'
import AccountPlanDialog from '../../components/accounts/AccountPlanDialog.vue'
import AccountResourcesCard from '../../components/accounts/AccountResourcesCard.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useCallerRole } from '../../composables/useCallerRole'
import { useAuthStore } from '../../stores/auth'
import { useLocaleStore } from '../../stores/locale'
import { useViewAsStore } from '../../stores/viewAs'
import { takeFlashNotice } from '../../utils/flashNotice'

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The account's identity, from `/accounts/:id`. */
  id: string
}>()

/**
 * The one dialog that is open over the page, or `null` when none is. The six are mutually
 * exclusive, so they share one value: two questions can never be asked at once.
 */
type AccountDialog = 'suspend' | 'reactivate' | 'delete' | 'plan' | 'owner' | 'ownerEmail' | null

const { t } = useI18n()
const router = useRouter()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()
const authStore = useAuthStore()
const viewAsStore = useViewAsStore()
const { isAdmin } = useCallerRole()

/** Which dialog the operator has opened: a confirmation, the deletion, or one of the three edits. */
const dialog: Ref<AccountDialog> = ref(null)

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the panel answered that it has no such account for the caller. */
const isMissing: ComputedRef<boolean> = computed(() => {
  return accountsStore.selectedState === 'loaded' && accountsStore.selected === null
})

/** Whether the account is still on its way, so the details draw placeholders. */
const isLoading: ComputedRef<boolean> = computed(() => {
  return accountsStore.selectedState === 'loading'
})

/** The confirmation's title, naming the account and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const account = accountsStore.selected?.name ?? ''
  return dialog.value === 'suspend'
    ? t('accounts.detail.confirmSuspendTitle', { account })
    : t('accounts.detail.confirmReactivateTitle', { account })
})

/** The confirm button's label, naming the action rather than a bare "Confirm" (#218). */
const confirmationLabel: ComputedRef<string> = computed(() => {
  return dialog.value === 'suspend'
    ? t('accounts.detail.confirmSuspendAction')
    : t('accounts.detail.confirmReactivateAction')
})

/** The consequence the confirmation asks the operator to weigh. */
const confirmationText: ComputedRef<string> = computed(() => {
  return dialog.value === 'suspend'
    ? t('accounts.detail.confirmSuspend')
    : t('accounts.detail.confirmReactivate')
})

/**
 * Starts an action, which then waits for confirmation.
 * @param action The action the operator clicked.
 * @returns Nothing; the page switches to its confirming state.
 */
const ask = (action: 'suspend' | 'reactivate'): void => {
  dialog.value = action
}

/**
 * Abandons a pending action.
 * @returns Nothing; the page returns to its normal state.
 */
const cancel = (): void => {
  dialog.value = null
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
  const action = dialog.value

  if (action === 'suspend') {
    await accountsStore.suspend(props.id)
  } else if (action === 'reactivate') {
    await accountsStore.reactivate(props.id)
  }

  dialog.value = null
}

/**
 * Opens the deletion dialog, replacing any dialog still on screen so two
 * questions are never being asked at once.
 * @returns Nothing.
 */
const askDelete = (): void => {
  dialog.value = 'delete'
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

  dialog.value = null
  await router.push({ name: 'accounts' })
}

/**
 * Opens one of the three edit dialogs, replacing any dialog still on screen.
 * @param field Which edit to open.
 * @returns Nothing.
 */
const openEdit = (field: 'plan' | 'owner' | 'ownerEmail'): void => {
  dialog.value = field
}

/**
 * Closes the edit dialog the panel accepted, and says what changed.
 * @returns Nothing.
 */
const editSaved = (): void => {
  const saved = dialog.value
  if (saved === 'plan') {
    notice.value = t('accounts.edit.planSaved')
  } else if (saved === 'owner') {
    notice.value = t('accounts.edit.ownerSaved')
  } else {
    notice.value = t('accounts.edit.ownerEmailSaved')
  }
  dialog.value = null
}

/**
 * Starts viewing the panel as this account's login (issue #317): the panel issues a view-as token,
 * the auth store holds it in place of the operator's, and the operator lands on the account's
 * dashboard with the "viewing as" banner above it. A refusal stays on this page, in the alert below
 * the heading. Whether the operator may do this at all is the panel's decision, not this page's.
 * @returns Resolves once the operator is viewing, or once the refusal is shown.
 */
const viewAs = async (): Promise<void> => {
  const session = await viewAsStore.start(props.id)
  if (session === null) {
    return
  }

  authStore.adoptViewAs(session)
  await router.push({ name: 'system-status' })
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(async () => {
  notice.value = takeFlashNotice()
  // The summary is its own read: a failed count shows in its card and never blanks the account.
  await Promise.all([accountsStore.loadOne(props.id), accountsStore.loadResourceSummary(props.id)])
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiLoadingRegion :state="accountsStore.selectedState">
      <!-- An account the panel could not read: its refusal, verbatim, or that it was unreachable. -->
      <template #error>
        <UiAlert variant="error">{{ accountsStore.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <UiEmptyState
        v-if="isMissing"
        :title="t('accounts.detail.notFoundTitle')"
        :description="t('accounts.detail.notFoundDescription')"
      />

      <div v-else class="flex flex-col gap-4">
        <!-- The name is an identifier (a lowercase Linux user name), so the title is set in the mono
             face rather than the display one: the delete dialog asks for it typed exactly, and the
             mono face shows it as the exact string to type. -->
        <UiPageHeading
          :title="accountsStore.selected?.name ?? ''"
          :subtitle="accountsStore.selected?.primaryDomain ?? ''"
          :loading="isLoading"
          mono
        >
          <template #actions>
            <UiLink :to="{ name: 'accounts' }" icon="chevronLeft">{{ t('accounts.detail.backToList') }}</UiLink>
            <UiButton variant="secondary" :disabled="isLoading || viewAsStore.acting" @click="viewAs">
              {{ viewAsStore.acting ? t('viewAs.starting') : t('viewAs.action') }}
            </UiButton>
          </template>
        </UiPageHeading>

        <!-- What needs attention comes first, before the details: a refusal the panel sent, then a
             suspended account. The reactivation control itself is in the
             actions card and is NOT repeated here, so the page has one button with that name. -->
        <UiAlert v-if="!isLoading && accountsStore.errorMessage !== null" variant="error">
          {{ accountsStore.errorMessage }}
        </UiAlert>

        <!-- A refused "View as", in the panel's own words. -->
        <UiAlert v-if="viewAsStore.errorMessage !== null" variant="error">
          {{ viewAsStore.errorMessage }}
        </UiAlert>

        <UiAlert v-if="!isLoading && accountsStore.selected?.status === 'suspended'" variant="warning">
          {{ t('accounts.detail.suspendedNotice') }}
        </UiAlert>

        <div class="grid gap-4 lg:grid-cols-3">
          <AccountDetailsCard
            class="lg:col-span-2"
            :account="accountsStore.selected"
            :locale="localeStore.current"
          />
          <AccountActionsCard
            :status="accountsStore.selected?.status ?? null"
            :can-change-owner="isAdmin"
            @suspend="ask('suspend')"
            @reactivate="ask('reactivate')"
            @edit-plan="openEdit('plan')"
            @edit-owner="openEdit('owner')"
            @edit-owner-email="openEdit('ownerEmail')"
            @delete="askDelete"
          />
        </div>
      </div>
    </UiLoadingRegion>

    <!-- Outside the account's region: the summary is its own read with its own loading state, and
         a failed count shows in its card without ever blanking the account. -->
    <AccountResourcesCard v-if="accountsStore.selectedState !== 'error' && !isMissing" :read="accountsStore.resourceSummary" />

    <template v-if="accountsStore.selectedState === 'loaded' && accountsStore.selected !== null">
      <AccountInvitationCard v-if="isAdmin" :account-id="id" :status="accountsStore.selected.status" />
      <AccountPersonalDataExport v-if="isAdmin" :account-id="id" />

      <!-- `:open` is bound to a value that really changes, and this dialog needs no `v-if`: it has
           no props that are only valid while it is open, and `UiModal` renders nothing while
           closed. The deletion dialog below cannot say the same — its `account` prop is
           `accountsStore.selected`, which is nullable — so it is mounted with `v-if` at the moment
           it opens, and `UiModal`s open-watcher is `immediate` so that case works too. -->
      <UiConfirm
        :open="dialog === 'suspend' || dialog === 'reactivate'"
        :title="confirmationTitle"
        :question="confirmationText"
        :confirm-label="confirmationLabel"
        :cancel-label="t('common.cancel')"
        :close-label="t('common.close')"
        :acting="accountsStore.acting"
        :acting-label="t('accounts.detail.working')"
        :destructive="dialog === 'suspend'"
        @close="cancel"
        @confirm="confirm"
      />

      <!-- Mounted only while open, for the same reason as the deletion dialog below: `account` is
           required and `accountsStore.selected` is nullable. -->
      <AccountPlanDialog
        v-if="dialog === 'plan'"
        :open="true"
        :account="accountsStore.selected"
        @close="cancel"
        @saved="editSaved"
      />
      <AccountOwnerDialog
        v-if="dialog === 'owner'"
        :open="true"
        :account="accountsStore.selected"
        @close="cancel"
        @saved="editSaved"
      />
      <AccountOwnerEmailDialog
        v-if="dialog === 'ownerEmail'"
        :open="true"
        :account="accountsStore.selected"
        @close="cancel"
        @saved="editSaved"
      />

      <!-- `v-if` because `account` is required and `accountsStore.selected` is nullable; the
           dialog is created with `open` already true, which `UiModal`s immediate open-watcher
           handles (focus enters, Escape reaches it, focus returns to the button that opened it). -->
      <AccountDeleteDialog
        v-if="dialog === 'delete'"
        :open="true"
        :account="accountsStore.selected"
        @close="cancel"
        @confirmed="remove"
      />
    </template>
    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
