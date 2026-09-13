<script setup lang="ts">
/**
 * The confirmation for the most destructive operation the panel offers: deleting a hosting account
 * together with its system user, its home directory, its sites and their certificates, its databases
 * and its SFTP and FTPS logins.
 *
 * **The list on screen is the list of subscribers, and it is kept that way on purpose.** Seven
 * modules handle `AccountDeleting` — Identity, Sites, Ssl, Databases, Sftp, Ftp and Backups — and a
 * confirmation that names only some of them reads as exhaustive while being incomplete, which is
 * worse on an irreversible screen than saying nothing specific at all. Sites and certificates were
 * missing here: the Sites subscriber takes the account's vhosts off the web server through the same
 * rpc a single site deletion uses, and that rpc purges the domain's certificate material, while the
 * Ssl subscriber drops the rows. Backups is the one subscriber whose effect is not a bullet in this
 * list, because it is the whole section below.
 *
 * **Why a typed confirmation and not the inline "Yes, do it" this page used to show.** The restore
 * dialog argued it first and the argument holds harder here: a second click is satisfied by the
 * same muscle that made the first one, and by a mis-aimed row; typing the account's own name is
 * not, and the value typed is the value that names what is about to be destroyed. A restore
 * replaces an account's files and databases from a copy that still exists afterwards; a deletion
 * removes the account itself, and the only thing left pointing at what it held is the final backup.
 * Confirming the greater loss with the lesser ceremony was the mismatch this dialog corrects.
 *
 * Unlike the restore's field, this one has NO server-side counterpart — `DeleteAccountCommand`
 * carries no confirmation string — so the check is entirely the panel's, and it is stated as such
 * rather than dressed up as an authorization decision.
 *
 * **What it says about the final backup, and why each sentence is here.** Deleting takes a final
 * copy as step 0, BEFORE anything is released, and a copy that cannot be made REFUSES the deletion
 * and leaves the account exactly as it was (`DeleteAccountCommandHandler`, step 0). An operator
 * confirming this dialog was told none of that: they could not tell a refusal from a failure, and
 * they could not know a copy would outlive the account they were removing. All three facts are on
 * screen now — the copy is taken first, a failure refuses rather than proceeds, and the copy stays
 * after the account is gone.
 *
 * **There is no "skip the final backup" control, deliberately.** The command accepts
 * `SkipFinalBackup` and audits it under its own action, and it stays off this screen: it is the one
 * path that throws away the last copy on purpose, and putting it beside the sentence promising a
 * copy is taken would hand the operator a one-click way to negate the promise the dialog is built
 * around. Nothing is lost by leaving it out — a destination that refuses is a fault to fix and
 * retry, not a reason to delete uncopied — and the flag remains available to the API for callers
 * that are not a person clicking (the query parameter on `DELETE /api/v1/accounts/{id}`).
 *
 * **The no-module branch is the honest one and is the default.** When the catalogue does not report
 * an enabled Backups module the dialog says no copy will be taken, which is exactly what the
 * handler does (`FinalBackupSkippedNoModule`). A catalogue that has not loaded reads the same way:
 * the failure direction of an advisory check on a destructive screen is to promise LESS safety than
 * exists, never more (rules/security.md — the client check is advice, and it must not overstate).
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useModuleAccess } from '../../composables/useModuleAccess'
import type { Account } from '../../types/account'

/** What the dialog is being opened for. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page. */
  open: boolean
  /** The account that would be removed. */
  account: Account
}>()

/** Events emitted by the dialog. */
const emit = defineEmits<{
  /**
   * Fired when the dialog should be closed; the page owns the open state.
   * @param e The event name.
   */
  (e: 'close'): void
  /**
   * Fired when the operator has confirmed by typing the account's name. The PAGE performs the
   * deletion, not this dialog.
   *
   * That split is not a preference. Deleting clears the store's `selected`, which unmounts the
   * branch this dialog lives in; Vue flushes that update before the `await` in a handler here would
   * resume, so an event emitted after the call would be emitted by a component that no longer
   * exists — measured, as a deletion that succeeded and never navigated.
   * @param e The event name.
   */
  (e: 'confirmed'): void
}>()

const { t } = useI18n()
const store = useAccountsStore()
const moduleAccess = useModuleAccess()

/** What the operator has typed into the confirmation field. Never prefilled. */
const typed: Ref<string> = ref('')

/**
 * Whether this panel will take a final copy before it releases anything.
 *
 * Read from the module catalogue rather than assumed, because the handler's own branch is the same
 * question: no Backups module means no final backup, recorded as `FinalBackupSkippedNoModule`.
 */
const takesFinalBackup: ComputedRef<boolean> = computed(() => {
  return moduleAccess.canUse('backups')
})

/**
 * Whether the typed confirmation may be sent.
 *
 * Exact and case-sensitive: a near-miss is a mis-read name, and accepting one would return the
 * dialog to the single gesture it exists to replace.
 */
const confirmed: ComputedRef<boolean> = computed(() => {
  return typed.value === props.account.name
})

/**
 * Hands the confirmed deletion to the page. A refusal leaves the dialog open, because the panel's
 * message is something the operator has to read.
 * @returns Nothing.
 */
const submit = (): void => {
  if (!confirmed.value || store.acting) {
    return
  }

  // Spent the moment it is used: a confirmation left in the field would put a second deletion one
  // click away on the arm where the first one was refused.
  typed.value = ''
  emit('confirmed')
}

/**
 * Closes the dialog, unless a deletion is in flight — the cascade continues on the server whatever
 * the browser does, and a dialog that vanished mid-deletion would leave the operator with no report.
 * @returns Nothing.
 */
const close = (): void => {
  if (store.acting) {
    return
  }
  emit('close')
}

// A dialog opened again must never open with a confirmation already in it. `immediate`, because the
// page mounts this with `v-if` at the moment it opens, so `open` is already true on the first
// render and a plain watcher would never fire at all.
watch(
  (): boolean => {
    return props.open
  },
  (isOpen: boolean): void => {
    if (isOpen) {
      typed.value = ''
    }
  },
  { immediate: true },
)
</script>

<template>
  <UiModal
    :open="open"
    :title="t('accounts.delete.title', { account: account.name })"
    :close-label="t('common.close')"
    :dismissible="!store.acting"
    @close="close"
  >
    <UiSpinner v-if="store.acting" :label="t('accounts.delete.working', { account: account.name })" />

    <div v-else class="flex flex-col gap-3">
      <UiAlert v-if="store.errorMessage !== null" variant="error">
        {{ store.errorMessage }}
      </UiAlert>
      <p v-if="store.errorMessage !== null" class="text-sm">
        {{ t('accounts.delete.refusedNote') }}
      </p>

      <UiSectionHeading :title="t('accounts.delete.removedHeading')" />
      <ul class="list-disc pl-5">
        <li>{{ t('accounts.delete.removedUser') }}</li>
        <li>{{ t('accounts.delete.removedHome') }}</li>
        <li>{{ t('accounts.delete.removedSites') }}</li>
        <li>{{ t('accounts.delete.removedCertificates') }}</li>
        <li>{{ t('accounts.delete.removedDatabases') }}</li>
      </ul>

      <UiSectionHeading :title="t('accounts.delete.backupHeading')" />
      <!-- The three facts the operator was never told: the copy is taken FIRST, a copy that cannot
           be made refuses the deletion outright, and the copy stays behind afterwards. The
           alternative branch is not a softer wording of the same thing — it is the opposite fact,
           and a panel without the Backups module must not imply a safety net it does not have. -->
      <template v-if="takesFinalBackup">
        <p>{{ t('accounts.delete.backupFirst') }}</p>
        <p class="font-medium text-text-primary">{{ t('accounts.delete.backupRefuses') }}</p>
        <p class="text-sm">{{ t('accounts.delete.backupOutlives') }}</p>
      </template>
      <p v-else class="font-medium text-text-primary">{{ t('accounts.delete.backupNoModule') }}</p>

      <p class="font-medium text-text-primary">{{ t('accounts.delete.pointOfNoReturn') }}</p>

      <UiInput
        v-model="typed"
        :label="t('accounts.delete.confirmLabel')"
        :required="true"
        autocomplete="off"
      />
      <p class="text-sm">{{ t('accounts.delete.confirmHint', { name: account.name }) }}</p>
    </div>

    <template #footer>
      <template v-if="!store.acting">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton variant="destructive" :disabled="!confirmed" @click="submit">
          {{ t('accounts.delete.submit') }}
        </UiButton>
      </template>
    </template>
  </UiModal>
</template>
