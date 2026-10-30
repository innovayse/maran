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
 * **The plan and the owner's address are edited in place** ({@link AccountPlanDialog},
 * {@link AccountOwnerEmailDialog}, #216). Both are refused by the panel when they cannot be applied
 * — a plan the account's disk usage does not fit, an address another login holds — and the dialog
 * shows that sentence; this page only says, on success, what changed.
 *
 * **The owner's invitation lives in {@link AccountInvitationCard}**, below the details (#78): where
 * it stands, a one-time link to hand over by hand (the way in on a panel that cannot send mail),
 * and "Resend invitation" as the recovery path for a login the owner never reached. All of it is
 * administrator-only on the server (`InvitationsController`); this page adds no matching role check
 * of its own (rules/architecture.md — the endpoint is the boundary).
 *
 * **While the account loads**, the heading and the details card are drawn already, with their real
 * terms and a placeholder line in place of each value ({@link UiLoadingRegion},
 * `UiDescriptionList loading`), so the account lands in the space held for it. The resources card
 * reads its own summary and says on its own how that read stands.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter, RouterLink } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiToast from '../../components/ui/UiToast.vue'
import AccountDeleteDialog from '../../components/accounts/AccountDeleteDialog.vue'
import AccountInvitationCard from '../../components/accounts/AccountInvitationCard.vue'
import AccountOwnerEmailDialog from '../../components/accounts/AccountOwnerEmailDialog.vue'
import AccountPlanDialog from '../../components/accounts/AccountPlanDialog.vue'
import AccountResourcesCard from '../../components/accounts/AccountResourcesCard.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import { takeFlashNotice } from '../../utils/flashNotice'

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The account's identity, from `/accounts/:id`. */
  id: string
}>()

/** The lifecycle action awaiting a yes/no answer, or `null` when none is. */
type PendingAction = 'suspend' | 'reactivate' | null

/** The edit dialog that is open, or `null` when none is. */
type OpenEdit = 'plan' | 'ownerEmail' | null

const { t } = useI18n()
const router = useRouter()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()

/** Which action the operator has started and is being asked to confirm. */
const pending: Ref<PendingAction> = ref(null)

/** Whether the deletion dialog is open. */
const deleting: Ref<boolean> = ref(false)

/** Which edit dialog is open. */
const editingField: Ref<OpenEdit> = ref(null)

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
  return pending.value === 'suspend'
    ? t('accounts.detail.confirmSuspendTitle', { account })
    : t('accounts.detail.confirmReactivateTitle', { account })
})

/** The confirm button's label, naming the action rather than a bare "Confirm" (#218). */
const confirmationLabel: ComputedRef<string> = computed(() => {
  return pending.value === 'suspend'
    ? t('accounts.detail.confirmSuspendAction')
    : t('accounts.detail.confirmReactivateAction')
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

/**
 * Opens one of the two edit dialogs, clearing any confirmation still on screen.
 * @param field Which edit to open.
 * @returns Nothing.
 */
const openEdit = (field: Exclude<OpenEdit, null>): void => {
  pending.value = null
  editingField.value = field
}

/**
 * Closes the open edit dialog without a change.
 * @returns Nothing.
 */
const closeEdit = (): void => {
  editingField.value = null
}

/**
 * Closes the edit dialog the panel accepted, and says what changed.
 * @returns Nothing.
 */
const editSaved = (): void => {
  notice.value = editingField.value === 'plan' ? t('accounts.edit.planSaved') : t('accounts.edit.ownerEmailSaved')
  editingField.value = null
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
          :eyebrow="t('accounts.list.heading')"
          :title="accountsStore.selected?.name ?? ''"
          :subtitle="accountsStore.selected?.primaryDomain ?? ''"
          :loading="isLoading"
          mono
        >
          <template #actions>
            <!-- A way back is navigation, so it looks like a link, not a button (#218). -->
            <RouterLink
              :to="{ name: 'accounts' }"
              class="inline-flex items-center gap-1 rounded-control text-sm font-medium text-accent-text underline underline-offset-4 hover:no-underline focus-visible:shadow-focus focus-visible:outline-none"
            >
              <UiIcon name="chevronLeft" size="sm" />
              {{ t('accounts.detail.backToList') }}
            </RouterLink>
          </template>
        </UiPageHeading>

        <!-- What needs attention comes first, before the details: a refusal the panel sent, then a
             suspended account. The reactivation control itself is in the
             actions card and is NOT repeated here, so the page has one button with that name. -->
        <UiAlert v-if="!isLoading && accountsStore.errorMessage !== null" variant="error">
          {{ accountsStore.errorMessage }}
        </UiAlert>

        <UiAlert v-if="!isLoading && accountsStore.selected?.status === 'suspended'" variant="warning">
          {{ t('accounts.detail.suspendedNotice') }}
        </UiAlert>

        <div class="grid gap-4 lg:grid-cols-3">
          <UiCard class="lg:col-span-2">
            <!-- The values are read only once loaded: while loading each item draws a placeholder
                 line in place of its slot. -->
            <UiDescriptionList :loading="isLoading">
              <UiDescriptionItem :term="t('common.name')" mono>
                {{ accountsStore.selected?.name }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('accounts.detail.primaryDomainLabel')" mono>
                {{ accountsStore.selected?.primaryDomain }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('accounts.detail.statusLabel')">
                <UiBadge
                  v-if="accountsStore.selected !== null"
                  :variant="accountsStore.selected.status === 'active' ? 'success' : 'warning'"
                >
                  {{ t(`accounts.status.${accountsStore.selected.status}`) }}
                </UiBadge>
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('accounts.detail.planLabel')">
                {{ accountsStore.selected?.planDisplayName }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('accounts.detail.ownerEmailLabel')" mono>
                {{ accountsStore.selected?.ownerEmail ?? t('accounts.detail.ownerEmailUnknown') }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('accounts.detail.createdAtLabel')" mono>
                {{ accountsStore.selected === null ? '' : formatDate(accountsStore.selected.createdAt, localeStore.current) }}
              </UiDescriptionItem>
            </UiDescriptionList>
          </UiCard>

          <UiCard class="flex flex-col gap-4">
            <UiSectionHeading :title="t('common.actions')" />
            <!-- Which actions apply depends on the account's status, so none is offered before it
                 is known; a placeholder holds the row. -->
            <UiSkeleton v-if="isLoading" shape="block" />
            <div v-else class="flex flex-wrap items-center gap-2">
              <UiButton
                v-if="accountsStore.selected?.status === 'active'"
                variant="secondary"
                @click="ask('suspend')"
              >
                {{ t('accounts.detail.suspend') }}
              </UiButton>
              <UiButton v-else variant="secondary" @click="ask('reactivate')">
                {{ t('accounts.detail.reactivate') }}
              </UiButton>
              <UiButton variant="secondary" @click="openEdit('plan')">
                {{ t('accounts.edit.planAction') }}
              </UiButton>
              <UiButton variant="secondary" @click="openEdit('ownerEmail')">
                {{ t('accounts.edit.ownerEmailAction') }}
              </UiButton>
            </div>
            <!-- Deletion sits apart, under a rule: it is the one action that cannot be undone, and it
                 must not read as a sibling of the two above it. -->
            <div v-if="!isLoading" class="border-t border-border-subtle pt-4">
              <UiButton variant="destructive" @click="askDelete">
                {{ t('common.delete') }}
              </UiButton>
            </div>
          </UiCard>
        </div>
      </div>
    </UiLoadingRegion>

    <!-- Outside the account's region: the summary is its own read with its own loading state, and
         a failed count shows in its card without ever blanking the account. -->
    <AccountResourcesCard v-if="accountsStore.selectedState !== 'error' && !isMissing" :read="accountsStore.resourceSummary" />

    <template v-if="accountsStore.selectedState === 'loaded' && accountsStore.selected !== null">
      <AccountInvitationCard :account-id="id" :suspended="accountsStore.selected.status === 'suspended'" />

      <!-- `:open` is bound to a value that really changes, and this dialog needs no `v-if`: it has
           no props that are only valid while it is open, and `UiModal` renders nothing while
           closed. The deletion dialog below cannot say the same — its `account` prop is
           `accountsStore.selected`, which is nullable — so it is mounted with `v-if` at the moment
           it opens, and `UiModal`s open-watcher is `immediate` so that case works too. -->
      <UiConfirm
        :open="pending !== null"
        :title="confirmationTitle"
        :question="confirmationText"
        :confirm-label="confirmationLabel"
        :cancel-label="t('common.cancel')"
        :close-label="t('common.close')"
        :acting="accountsStore.acting"
        :acting-label="t('accounts.detail.working')"
        :destructive="pending === 'suspend'"
        @close="cancel"
        @confirm="confirm"
      />

      <!-- Mounted only while open, for the same reason as the deletion dialog below: `account` is
           required and `accountsStore.selected` is nullable. -->
      <AccountPlanDialog
        v-if="editingField === 'plan'"
        :open="true"
        :account="accountsStore.selected"
        @close="closeEdit"
        @saved="editSaved"
      />
      <AccountOwnerEmailDialog
        v-if="editingField === 'ownerEmail'"
        :open="true"
        :account="accountsStore.selected"
        @close="closeEdit"
        @saved="editSaved"
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
    <div class="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex justify-end sm:left-auto">
      <UiToast v-if="notice !== null" variant="success" :close-label="t('common.close')" @dismiss="dismissNotice">
        {{ notice }}
      </UiToast>
    </div>
  </section>
</template>
