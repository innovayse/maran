<script setup lang="ts">
/**
 * The confirmation for the one operation in this module that can destroy a working account:
 * restoring an account from one of its backups.
 *
 * **It is deliberately not shaped like the create form.** Taking a backup is additive and a button
 * is the whole of it; a restore replaces an account's home and drops every database in the copy,
 * and the point of no return is the first `DROP DATABASE` — after which the only thing that can put
 * a database back is a reload of a dump the agent took moments earlier, and that reload can itself
 * fail. So this dialog states what is replaced, what is NOT, and what the safety net is (a backup
 * of the account as it is now, taken first, without which the restore does not start), before it
 * will accept anything at all.
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
 * **The untouched arm itself carries two sentences, not one.** The panel answers that arm for a
 * refused confirmation AND for a copy the server could not use — a digest that did not match, a
 * tampered or truncated artifact. Retyping the account name never fixes the second, so telling the
 * operator to "correct the confirmation and try again" whatever had failed would send them, during a
 * recovery, to the one thing that cannot help. The instruction to retype is shown for the two
 * confirmation codes only; every other untouched ending is told that nothing was changed and that
 * retyping will not change the answer.
 *
 * **A restore can be narrowed to one database or one site (issue #160), and nothing else about the
 * ceremony changes.** The copy is still replaced over what is there, the account name is still
 * typed, the safety copy is still taken first and the point of no return is still the first drop,
 * so the warnings stay and only the "what is replaced / what is kept" lists change with the choice.
 * The names come from the panel (`BackupRestoreScopeFields`), the request carries at most one of
 * them, and whichever kind was sent is remembered for the report, because the store's outcome says
 * how many databases were replaced and not which kind of restore it was.
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
import BackupRestoreScopeFields from './BackupRestoreScopeFields.vue'
import { useBackupsStore } from '../../stores/backups'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import { restoreConfirmationWasRefused } from '../../utils/restoreConfirmationWasRefused'
import type { Backup, RestoreBackupRequest, RestoreScopeKind } from '../../types/backup'

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

/** Which part of the account the restore replaces; the whole account until the operator chooses otherwise. */
const scopeKind: Ref<RestoreScopeKind> = ref('whole')

/** The database chosen for a database restore, or an empty string. */
const chosenDatabase: Ref<string> = ref('')

/** The site chosen for a site restore, or an empty string. */
const chosenSite: Ref<string> = ref('')

/**
 * The kind and the name of the restore last sent, kept for the report: the store's outcome says how
 * many databases were replaced and not which kind of restore was asked for.
 */
const sent: Ref<{ kind: RestoreScopeKind; name: string }> = ref({ kind: 'whole', name: '' })

/** Whether a restore is in flight, which holds the dialog open and the form out of reach. */
const running: ComputedRef<boolean> = computed(() => {
  return store.restoreRun.state === 'running'
})

/**
 * Whether the failure on screen is one the operator can fix by typing again.
 *
 * Only a refused confirmation is. Every other untouched ending — an unusable copy above all — is
 * not, and the sentence under the server's message says so instead of asking for a retype.
 */
const confirmationRefused: ComputedRef<boolean> = computed(() => {
  return store.restoreRun.state === 'untouched' && restoreConfirmationWasRefused(store.restoreRun.code)
})

/**
 * Whether the confirmation form is still the thing the dialog is showing: before an attempt, and
 * after any ending that left the account as it was or told the browser nothing.
 */
const asking: ComputedRef<boolean> = computed(() => {
  const state = store.restoreRun.state
  return state === 'idle' || state === 'unanswered' || state === 'untouched'
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

/**
 * Whether the part to restore is fully chosen: a whole restore always is, a narrower one once its
 * name is picked. Advice only — the server re-checks the name against the account's own rows.
 */
const scopeChosen: ComputedRef<boolean> = computed(() => {
  if (scopeKind.value === 'database') {
    return chosenDatabase.value !== ''
  }
  if (scopeKind.value === 'site') {
    return chosenSite.value !== ''
  }
  return true
})

/** The name of the chosen part, or an empty string for the whole account. */
const chosenName: ComputedRef<string> = computed(() => {
  if (scopeKind.value === 'database') {
    return chosenDatabase.value
  }
  return scopeKind.value === 'site' ? chosenSite.value : ''
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
  if (!confirmed.value || !scopeChosen.value || running.value) {
    return
  }
  // At most one narrowing field, and none for the whole account: the body of a whole restore is
  // exactly what it was before restores could be narrowed.
  const request: RestoreBackupRequest = { confirmAccountUsername: typed.value }
  if (scopeKind.value === 'database') {
    request.databaseName = chosenDatabase.value
  } else if (scopeKind.value === 'site') {
    request.siteDomain = chosenSite.value
  }
  sent.value = { kind: scopeKind.value, name: chosenName.value }
  await store.restore(props.backup.id, request)
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
  if (running.value) {
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
      scopeKind.value = 'whole'
      chosenDatabase.value = ''
      chosenSite.value = ''
      store.clearRestore()
      void store.loadRestoreChoices(props.backup.id)
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
    :dismissible="!running"
    @close="close"
  >
    <UiSpinner v-if="running" :label="t('backups.restore.working', { account: subject })" />

    <!-- The changed arm: the account HAS been altered. The server's own message first, then what
         that means, then the one instruction that matters — in an amber notice, because it is the
         thing the operator must not skip past. -->
    <div v-else-if="store.restoreRun.state === 'changed'" class="flex flex-col gap-4">
      <UiAlert variant="error">{{ store.restoreRun.message }}</UiAlert>
      <UiSectionHeading :title="t('backups.restore.changedTitle')" />
      <p>{{ t('backups.restore.changedBody') }}</p>
      <!-- The counts, only when the server measured them. Absent on a truncated stream and from an
           older panel, and the copy claims no more than the counts carry: how many were replaced
           WHEN IT STOPPED, on the server's own statement — not which ones, and not that those are
           now consistent. A restore with no databases in scope shows no databases line rather than
           an empty "0 of 0". -->
      <div
        v-if="store.restoreRun.partial !== null"
        class="flex flex-col gap-3 rounded-control border border-border-subtle bg-surface-2 p-4"
      >
        <p>{{ t('backups.restore.changedCountsIntro') }}</p>
        <UiDescriptionList>
          <UiDescriptionItem :term="t('backups.restore.changedFiles')">
            {{
              store.restoreRun.partial.filesRestored
                ? t('backups.restore.changedFilesReplaced')
                : t('backups.restore.changedFilesNotReplaced')
            }}
          </UiDescriptionItem>
          <UiDescriptionItem
            v-if="store.restoreRun.partial.databasesTotal > 0"
            :term="t('backups.restore.changedDatabases')"
            mono
          >
            {{
              t('backups.restore.changedDatabasesValue', {
                restored: store.restoreRun.partial.databasesRestored,
                total: store.restoreRun.partial.databasesTotal,
              })
            }}
          </UiDescriptionItem>
        </UiDescriptionList>
      </div>
      <UiAlert variant="warning">
        <p class="font-medium">{{ t('backups.restore.changedNoRetry') }}</p>
      </UiAlert>
      <p class="text-text-muted">{{ t('backups.restore.changedNoList') }}</p>
    </div>

    <!-- The restored arm exists only for a restore that replaced the whole account, so the home
         directory is stated as replaced without a flag to read, and the databases line is the one
         figure that varies. -->
    <div v-else-if="store.restoreRun.state === 'restored'" class="flex flex-col gap-4">
      <UiSectionHeading
        :title="
          sent.kind === 'whole'
            ? t('backups.restore.doneTitle', { account: subject })
            : t('backups.restore.donePartTitle', { name: sent.name })
        "
      />
      <div class="rounded-control border border-border-subtle bg-surface-2 p-4">
        <UiDescriptionList v-if="sent.kind === 'whole'">
          <UiDescriptionItem :term="t('backups.restore.doneFiles')">
            {{ t('backups.restore.doneFilesReplaced') }}
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('backups.restore.doneDatabases')" mono>
            {{ t('backups.restore.doneDatabasesValue', { count: store.restoreRun.outcome.databasesRestored }) }}
          </UiDescriptionItem>
        </UiDescriptionList>
        <UiDescriptionList v-else>
          <UiDescriptionItem
            :term="sent.kind === 'database' ? t('backups.restore.doneDatabase') : t('backups.restore.doneSite')"
            mono
          >
            {{ sent.name }}: {{ t('backups.restore.doneReplaced') }}
          </UiDescriptionItem>
        </UiDescriptionList>
      </div>
      <p class="text-text-muted">
        {{ sent.kind === 'whole' ? t('backups.restore.doneNote') : t('backups.restore.donePartNote') }}
      </p>
    </div>

    <div v-else class="flex flex-col gap-4">
      <UiAlert v-if="store.restoreRun.state === 'untouched'" variant="error">
        {{ store.restoreRun.message }}
      </UiAlert>
      <!-- Two sentences, because the two failures behind them need opposite instructions. A
           refused confirmation is corrected by typing again; a copy the server could not use is
           not, and telling an operator mid-recovery to "correct the confirmation" over a corrupt
           artifact sends them down a dead end no amount of retyping leaves. -->
      <template v-if="store.restoreRun.state === 'untouched'">
        <p v-if="confirmationRefused">
          {{ t('backups.restore.untouchedConfirmation') }}
        </p>
        <p v-else>{{ t('backups.restore.untouchedCopy') }}</p>
      </template>

      <!-- Which copy, and what it costs: the instant the copy was taken, and that everything newer
           is gone. Raised, so it reads as the headline of the dialog rather than one more paragraph. -->
      <div class="flex flex-col gap-1.5 rounded-control border border-border-subtle bg-surface-2 p-4">
        <p class="font-medium text-text-primary">
          {{ t('backups.restore.takenAt', { taken: takenAt }) }}
        </p>
        <p>{{ t('backups.restore.lostSince') }}</p>
      </div>

      <!-- What to restore comes before what it replaces, because the two lists below follow from it. -->
      <BackupRestoreScopeFields
        v-model:kind="scopeKind"
        v-model:database="chosenDatabase"
        v-model:site="chosenSite"
        :choices="store.restoreChoices"
        :disabled="running"
      />

      <div class="flex flex-col gap-2">
        <UiSectionHeading :title="t('backups.restore.replacedHeading')" />
        <ul class="list-disc space-y-1 pl-5 marker:text-text-muted">
          <li v-if="scopeKind === 'database'">
            {{ t('backups.restore.replacedOneDatabase', { name: chosenName || t('backups.restore.theChosenPart') }) }}
          </li>
          <li v-else-if="scopeKind === 'site'">
            {{ t('backups.restore.replacedOneSite', { name: chosenName || t('backups.restore.theChosenPart') }) }}
          </li>
          <template v-else>
            <li>{{ t('backups.restore.replacedHome') }}</li>
            <li>
              {{
                backup.databaseCount === 0
                  ? t('backups.restore.replacedNoDatabases')
                  : t('backups.restore.replacedDatabases', { count: backup.databaseCount }, backup.databaseCount)
              }}
            </li>
          </template>
        </ul>
      </div>

      <div class="flex flex-col gap-2">
        <UiSectionHeading :title="t('backups.restore.keptHeading')" />
        <ul class="list-disc space-y-1 pl-5 marker:text-text-muted">
          <li v-if="scopeKind === 'database'">{{ t('backups.restore.keptRestOfDatabase') }}</li>
          <li v-else-if="scopeKind === 'site'">{{ t('backups.restore.keptRestOfSite') }}</li>
          <li>{{ t('backups.restore.keptSites') }}</li>
          <li>{{ t('backups.restore.keptOwnership') }}</li>
        </ul>
        <p class="text-text-muted">{{ t('backups.restore.keptExplanation') }}</p>
      </div>

      <!-- The two sentences that decide whether this dialog is worth anything: there is a point
           after which the restore itself cannot be undone, and what the safety net is. The panel
           takes a `PreRestore` backup of the account as it stands now and refuses the restore if it
           cannot be made; the net holds the home and the databases only, which the sentence says.
           They are the one thing that needs attention, so they are the amber notice, directly above
           the field that commits to them. -->
      <UiAlert variant="warning">
        <div class="flex flex-col gap-2 font-medium">
          <p v-if="scopeKind === 'site'">{{ t('backups.restore.pointOfNoReturnSite') }}</p>
          <p v-else>{{ t('backups.restore.pointOfNoReturn') }}</p>
          <p>{{ t('backups.restore.preRestoreCopy') }}</p>
        </div>
      </UiAlert>

      <div class="flex flex-col gap-2">
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
        <p v-if="accountUsername !== null" class="text-text-muted">
          {{ t('backups.restore.confirmHint', { name: accountUsername }) }}
        </p>
        <p v-else class="text-text-muted">{{ t('backups.restore.confirmHintUnknown') }}</p>
      </div>
    </div>

    <template #footer>
      <template v-if="asking">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton variant="destructive" :disabled="!confirmed || !scopeChosen" @click="submit">
          {{ scopeKind === 'whole' ? t('backups.restore.submit') : t('backups.restore.submitPart') }}
        </UiButton>
      </template>
      <!-- The changed arm offers exactly one control, and it is not a retry. -->
      <UiButton v-else-if="!running" variant="secondary" @click="close">
        {{ t('common.close') }}
      </UiButton>
    </template>
  </UiModal>
</template>
