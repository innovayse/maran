<script setup lang="ts">
/**
 * The confirmation for the one operation in this module that can destroy a working account:
 * restoring an account from one of its backups.
 *
 * **It is deliberately not shaped like the create form.** Taking a backup is additive and a button
 * is the whole of it; a restore replaces an account's home and drops every database in the copy,
 * and the point of no return is the first `DROP DATABASE` — after which the only thing that can put
 * a database back is a reload of a dump the agent took moments earlier, and that reload can itself
 * fail. So this dialog states what is replaced, what is NOT, and that no copy is taken of the
 * account as it is now, before it will accept anything at all.
 *
 * **The confirmation is the account's own system user name, typed.** A second click is satisfied by
 * the same muscle that made the first one and by a mis-aimed row; typing the name is not, and the
 * value typed is the same value that names what is about to be overwritten — so the operator cannot
 * complete the gesture without having read WHICH account they are replacing. It is the same check
 * the backend makes (`RestoreBackupCommandHandler` compares it to the account's `Username`), which
 * is the point: the screen does not invent a second, weaker ceremony in front of the server's.
 *
 * The name is SHOWN and never prefilled. Showing it is necessary — an operator who cannot see the
 * target cannot know what they are destroying — and filling it in would leave a destructive
 * operation one click away again, which is the whole thing this dialog exists to prevent.
 *
 * The client-side match is advice and errs permissive (rules/security.md): when the panel's account
 * list does not name this account — a backup whose account has since been deleted is the real case —
 * the field is accepted as typed and the SERVER refuses it. A screen that blocked there would be a
 * client deciding an authorization question it cannot see the answer to.
 *
 * **A failed restore is two conditions, not one**, and they are rendered differently. Every code but
 * two means nothing was touched, so the dialog keeps the form and the operator may try again. The
 * two — `RestorePartial` and `RestoreTruncated` — mean the account HAS been changed, and the dialog
 * then offers no way to try again at all: a blind second attempt over a half-replaced account is how
 * the retry destroys what the first attempt left usable.
 *
 * **The untouched arm itself carries two sentences, not one.** It used to say "correct the
 * confirmation and try again" whatever had failed, and the panel answers that arm for a refused
 * confirmation AND for a copy the server could not use — a digest that did not match, a tampered or
 * truncated artifact. Retyping the account name has never fixed the second, so during a recovery
 * the screen was sending the operator to the one thing that cannot help. The instruction to retype
 * is now shown for the two confirmation codes only; every other untouched ending is told that
 * nothing was changed and that retyping will not change the answer.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { useBackupsStore } from '../../stores/backups'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import { restoreChangedTheAccount } from '../../utils/restoreChangedTheAccount'
import { restoreConfirmationWasRefused } from '../../utils/restoreConfirmationWasRefused'
import type { Backup } from '../../types/backup'

/** What the dialog is being opened for. */
const props = defineProps<{
  /** Whether the dialog is shown; owned by the page. */
  open: boolean
  /** The backup the account would be replaced from. */
  backup: Backup
  /**
   * The target account's system user name — the exact string that must be typed — or `null` when
   * the panel's account list does not name it. `null` is not a hypothetical: a backup outlives its
   * account, so a copy taken before a deletion has no account to name.
   */
  accountUsername: string | null
}>()

/** Events emitted by the dialog. */
const emit = defineEmits<{
  /** Fired when the dialog should be closed; the page owns the open state. */
  (e: 'close'): void
}>()

const { t } = useI18n()
const store = useBackupsStore()
const localeStore = useLocaleStore()

/** What the operator has typed into the confirmation field. Never prefilled. */
const typed: Ref<string> = ref('')

/** Whether the restore that has just been attempted left the account changed. */
const changed: ComputedRef<boolean> = computed(() => {
  return store.restoreErrorMessage !== null && restoreChangedTheAccount(store.restoreErrorCode)
})

/**
 * Whether the failure on screen is one the operator can fix by typing again.
 *
 * Only a refused confirmation is. Every other untouched ending — an unusable copy above all — is
 * not, and the sentence under the server's message says so instead of asking for a retype.
 */
const confirmationRefused: ComputedRef<boolean> = computed(() => {
  return restoreConfirmationWasRefused(store.restoreErrorCode)
})

/** Whether the dialog is reporting a restore that replaced the whole account. */
const succeeded: ComputedRef<boolean> = computed(() => {
  return store.restoreOutcome !== null
})

/** Whether the confirmation form is still the thing the dialog is showing. */
const asking: ComputedRef<boolean> = computed(() => {
  return !succeeded.value && !changed.value && !store.restoring
})

/**
 * Whether the typed confirmation may be sent.
 *
 * Exact and case-sensitive, matching the server's `Ordinal` comparison — a client that accepted a
 * near-miss would send a request it knows will be refused, and the operator would read the refusal
 * as the panel being broken rather than as their own typo.
 *
 * The permissive branch is the important one: with no known account name there is nothing to
 * compare against, so anything non-empty is allowed through and the server decides.
 */
const confirmed: ComputedRef<boolean> = computed(() => {
  if (props.accountUsername === null) {
    return typed.value.length > 0
  }
  return typed.value === props.accountUsername
})

/** When the copy about to be restored was taken, as an instant an operator can compare. */
const takenAt: ComputedRef<string> = computed(() => {
  return formatIsoTimestamp(props.backup.finishedAt ?? props.backup.startedAt, localeStore.current)
})

/**
 * The name the dialog addresses the account by. Falls back to the identifier only when the panel
 * has no name for it, because a dialog that named nothing would be a dialog about no account.
 */
const subject: ComputedRef<string> = computed(() => {
  return props.accountUsername ?? props.backup.accountId
})

/**
 * Sends the restore. The dialog stays open whatever happens: every ending here is something the
 * operator has to be told, and two of them are things they must not act on blindly.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  if (!confirmed.value || store.restoring) {
    return
  }
  await store.restore(props.backup.id, { confirmAccountUsername: typed.value })
  // Cleared whatever happened: a confirmation is spent once used, and leaving it filled would put a
  // second restore one click away — including on the arm where the account has just been changed.
  typed.value = ''
}

/**
 * Closes the dialog, unless a restore is in flight — the operation continues on the server whatever
 * the browser does, and a dialog that vanished mid-restore would leave the operator with no report.
 * @returns Nothing.
 */
const close = (): void => {
  if (store.restoring) {
    return
  }
  emit('close')
}

// A dialog opening on a second row must not show what happened on the first, and it must never open
// with a confirmation already in it.
//
// `immediate` is load-bearing, not a habit. The page mounts this component with `v-if` at the moment
// it opens, so `open` is already `true` on the first render and a plain watcher never fires at all —
// which left the outcome of a PREVIOUS restore, held in the store, on screen the instant a dialog was
// opened on another row. A mutation that prefilled the confirmation field survived the spec that
// forbids prefilling, which is what exposed it: the reset had never been running.
watch(
  (): boolean => {
    return props.open
  },
  (isOpen: boolean): void => {
    if (isOpen) {
      typed.value = ''
      store.clearRestore()
    }
  },
  { immediate: true },
)
</script>

<template>
  <UiModal
    :open="open"
    :title="t('backups.restore.title', { account: subject })"
    :close-label="t('common.close')"
    :dismissible="!store.restoring"
    @close="close"
  >
    <UiSpinner v-if="store.restoring" :label="t('backups.restore.working', { account: subject })" />

    <div v-else-if="changed" class="flex flex-col gap-3">
      <UiAlert variant="error">{{ store.restoreErrorMessage }}</UiAlert>
      <UiSectionHeading :title="t('backups.restore.changedTitle')" />
      <p>{{ t('backups.restore.changedBody') }}</p>
      <!-- The counts, only when the server measured them. Absent on a truncated stream and from an
           older panel, and the copy claims no more than the counts carry: how many were replaced
           WHEN IT STOPPED, on the server's own statement — not which ones, and not that those are
           now consistent. A restore with no databases in scope shows no databases line rather than
           an empty "0 of 0". -->
      <template v-if="store.restorePartial !== null">
        <p class="text-sm">{{ t('backups.restore.changedCountsIntro') }}</p>
        <UiDescriptionList>
          <UiDescriptionItem :term="t('backups.restore.changedFiles')">
            {{
              store.restorePartial.filesRestored
                ? t('backups.restore.changedFilesReplaced')
                : t('backups.restore.changedFilesNotReplaced')
            }}
          </UiDescriptionItem>
          <UiDescriptionItem
            v-if="store.restorePartial.databasesTotal > 0"
            :term="t('backups.restore.changedDatabases')"
            mono
          >
            {{
              t('backups.restore.changedDatabasesValue', {
                restored: store.restorePartial.databasesRestored,
                total: store.restorePartial.databasesTotal,
              })
            }}
          </UiDescriptionItem>
        </UiDescriptionList>
      </template>
      <p class="font-medium text-text-primary">{{ t('backups.restore.changedNoRetry') }}</p>
      <p class="text-sm">{{ t('backups.restore.changedNoList') }}</p>
    </div>

    <div v-else-if="succeeded && store.restoreOutcome !== null" class="flex flex-col gap-3">
      <UiSectionHeading :title="t('backups.restore.doneTitle', { account: subject })" />
      <UiDescriptionList>
        <UiDescriptionItem :term="t('backups.restore.doneFiles')">
          {{
            store.restoreOutcome.filesRestored
              ? t('backups.restore.doneFilesReplaced')
              : t('backups.restore.doneFilesUntouched')
          }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('backups.restore.doneDatabases')" mono>
          {{
            t('backups.restore.doneDatabasesValue', {
              restored: store.restoreOutcome.databasesRestored,
              total: store.restoreOutcome.databasesTotal,
            })
          }}
        </UiDescriptionItem>
      </UiDescriptionList>
      <p class="text-sm">{{ t('backups.restore.doneNote') }}</p>
    </div>

    <div v-else class="flex flex-col gap-3">
      <UiAlert v-if="store.restoreErrorMessage !== null" variant="error">
        {{ store.restoreErrorMessage }}
      </UiAlert>
      <!-- Two sentences, because the two failures behind them need opposite instructions. A
           refused confirmation is corrected by typing again; a copy the server could not use is
           not, and telling an operator mid-recovery to "correct the confirmation" over a corrupt
           artifact sends them down a dead end no amount of retyping leaves. -->
      <template v-if="store.restoreErrorMessage !== null">
        <p v-if="confirmationRefused" class="text-sm">
          {{ t('backups.restore.untouchedConfirmation') }}
        </p>
        <p v-else class="text-sm">{{ t('backups.restore.untouchedCopy') }}</p>
      </template>

      <p class="font-medium text-text-primary">
        {{ t('backups.restore.takenAt', { taken: takenAt }) }}
      </p>
      <p>{{ t('backups.restore.lostSince') }}</p>

      <UiSectionHeading :title="t('backups.restore.replacedHeading')" />
      <ul class="list-disc pl-5">
        <li>{{ t('backups.restore.replacedHome') }}</li>
        <li>{{ t('backups.restore.replacedDatabases', { databases: backup.databaseCount }) }}</li>
      </ul>

      <UiSectionHeading :title="t('backups.restore.keptHeading')" />
      <ul class="list-disc pl-5">
        <li>{{ t('backups.restore.keptSites') }}</li>
        <li>{{ t('backups.restore.keptOwnership') }}</li>
      </ul>
      <p class="text-sm">{{ t('backups.restore.keptExplanation') }}</p>

      <!-- The two sentences that decide whether this dialog is worth anything: there is a point
           after which nothing here can undo the operation, and there is no copy of the account as
           it stands now to fall back on. Nothing in the panel takes a pre-restore backup today —
           `BackupKind.PreRestore` exists and no code path produces one — so a screen implying a
           safety net would be inventing it. -->
      <p class="font-medium text-text-primary">{{ t('backups.restore.pointOfNoReturn') }}</p>
      <p class="font-medium text-text-primary">{{ t('backups.restore.noSafetyCopy') }}</p>

      <UiInput
        v-model="typed"
        :label="t('backups.restore.confirmLabel')"
        :required="true"
        autocomplete="off"
      />
      <!-- Two hints, because the honest instruction differs. When the panel names the account the
           operator is told exactly what to type; when it does not — a copy whose account has since
           been deleted — telling them to type the identifier shown above would be telling them to
           type the wrong thing, so they are told what the value IS and the server judges it. -->
      <p v-if="accountUsername !== null" class="text-sm">
        {{ t('backups.restore.confirmHint', { name: accountUsername }) }}
      </p>
      <p v-else class="text-sm">{{ t('backups.restore.confirmHintUnknown') }}</p>
    </div>

    <template #footer>
      <template v-if="asking">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton variant="destructive" :disabled="!confirmed" @click="submit">
          {{ t('backups.restore.submit') }}
        </UiButton>
      </template>
      <!-- The changed arm offers exactly one control, and it is not a retry. -->
      <UiButton v-else-if="!store.restoring" variant="secondary" @click="close">
        {{ t('common.close') }}
      </UiButton>
    </template>
  </UiModal>
</template>
