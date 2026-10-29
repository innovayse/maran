<script setup lang="ts">
/**
 * Backups screen: the take-a-backup-now form and the list of what has been taken. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under. State comes exclusively from the backups store; the page never touches the API
 * layer (rules/vue.md: API composables are called from stores only).
 *
 * One page rather than a list plus a detail page, because a backup has no detail to open: every
 * field the panel holds about one is a column here, and `GET /api/v1/backups/{id}` answers the
 * same shape the list already carries.
 *
 * **A row's commands live behind one menu, not a button each.** The last column holds a single
 * trigger whose accessible name carries the row's account, and the commands are inside it. A menu
 * even where a row offers only Delete: a running or failed row is offered no restore, so a table
 * that dropped the menu for a single command would change the shape of its last column from row to
 * row, and an operator would have to re-find the control on every line.
 *
 * **The two actions are not shaped alike.** Delete destroys a copy and is confirmed by
 * {@link ../../components/ui/UiConfirm.vue}, the panel's one yes/no question.
 * Restore destroys the ACCOUNT — it replaces the home and drops every database in the copy — so it
 * opens {@link ../../components/backups/BackupRestoreDialog.vue}, which states what is replaced and
 * what is not before it will accept a typed confirmation. The panel publishes
 * `POST /api/v1/backups/{id}/restore`, which is what the dialog calls.
 *
 * **Restore is offered on a completed backup and on no other.** A running or failed run produced no
 * artifact and the panel answers `BackupNotRestorable`, so a control on those rows could only ever
 * refuse — and a control that cannot act is the same promise as a control that does not exist,
 * made in the other direction.
 *
 * **The screen does not say where the bytes go beyond "this server".** The panel's create endpoint
 * takes no destination, and both the agent and the panel refuse a remote one, so a destination
 * column would be a field with one possible value and a destination picker would advertise a
 * choice that does not exist. Where that one place IS, and what the panel can and cannot be told
 * about it, is {@link ./BackupDestinationsPage.vue} — a screen of its own, linked from the heading,
 * because it describes the machine rather than the rows this table holds.
 *
 * **A failed backup is the screen's one attention item, and only while it is still true.** A
 * warning notice opens the screen when an account's most recent FINISHED run failed — a failed
 * copy is a copy that does not exist, the one fact a list of backups can most dangerously imply the
 * opposite of. It is not raised for every failed row ever taken: a failure from March that a
 * completed run has since superseded is history, and a notice that never clears teaches an
 * operator to stop reading it. A still-running run is not a finished one, so it neither raises the
 * notice nor clears it.
 *
 * **The table lives inside its own `UiCard` and nothing else is allowed to scroll the page.** At
 * 390px the eight columns are wider than the screen, so `UiTable` scrolls inside its own container
 * (the specs find that container as `table.parentElement`); the card has `min-w-0` for the same
 * reason, so a wide table can never size the content panel.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import BackupCreateForm from '../../components/backups/BackupCreateForm.vue'
import BackupRestoreDialog from '../../components/backups/BackupRestoreDialog.vue'
import BackupStatusBadge from '../../components/backups/BackupStatusBadge.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useBackupsStore } from '../../stores/backups'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { Backup, CreateBackupRequest } from '../../types/backup'

const { t } = useI18n()
const store = useBackupsStore()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()

/** The backup whose deletion is awaiting confirmation, or the empty string when none is. */
const pendingId: Ref<string> = ref('')

/**
 * The backup the restore dialog is open for, or `null` when it is closed.
 *
 * The whole backup rather than its id: the dialog has to state what the copy holds — when it was
 * taken and how many databases are in it — and re-finding the row from an id would be a lookup that
 * can miss while the dialog it feeds is already on screen.
 */
const restoreTarget: Ref<Backup | null> = ref(null)

/** Whether the panel answered successfully and reported no backups at all. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.backups.length === 0
})

/** The backup the confirmation is open for, or `null` when none is. */
const pendingBackup: ComputedRef<Backup | null> = computed(() => {
  return (
    store.backups.find((backup) => {
      return backup.id === pendingId.value
    }) ?? null
  )
})

/** The confirmation's accessible name, naming the copy that would be destroyed. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const pending = pendingBackup.value
  return pending === null
    ? ''
    : t('backups.list.confirmDeleteTitle', {
        account: accountName(pending),
        startedAt: formatIsoTimestamp(pending.startedAt, localeStore.current),
      })
})

/**
 * The backups whose account's most recent finished run failed, newest failure first.
 *
 * Grouped by account because the question an operator asks is "which accounts have no good copy
 * from their latest attempt", not "how many rows say Failed". A `running` run is skipped: it has no
 * outcome yet, so it can neither be the failure nor the success that supersedes one. Instants are
 * compared as parsed times, not as strings, because the panel is free to change the offset it
 * writes.
 */
const unrecoveredFailures: ComputedRef<Backup[]> = computed(() => {
  const latest = new Map<string, Backup>()
  for (const backup of store.backups) {
    if (backup.status === 'running') {
      continue
    }
    const seen = latest.get(backup.accountId)
    if (seen === undefined || Date.parse(backup.startedAt) > Date.parse(seen.startedAt)) {
      latest.set(backup.accountId, backup)
    }
  }
  return [...latest.values()]
    .filter((backup) => {
      return backup.status === 'failed'
    })
    .sort((a, b) => {
      return Date.parse(b.startedAt) - Date.parse(a.startedAt)
    })
})

/**
 * The sentence the attention notice says, or `null` when nothing needs attention.
 *
 * One failure is named — the account and the moment — because that is enough to act on. Several
 * are counted, since a list of names in a banner would be the table over again.
 */
const attentionMessage: ComputedRef<string | null> = computed(() => {
  const failures = unrecoveredFailures.value
  if (failures.length === 0) {
    return null
  }
  const [first] = failures
  if (failures.length === 1 && first !== undefined) {
    return t('backups.list.attentionOne', {
      account: accountName(first),
      startedAt: formatIsoTimestamp(first.startedAt, localeStore.current),
    })
  }
  return t('backups.list.attentionMany', { count: failures.length })
})

/**
 * Names the account a backup belongs to, for a column that would otherwise print a GUID.
 *
 * The live accounts list is asked first, and the backup's own `orphanedAccountUsername` second.
 * That order matters: while an account exists its current name is the truthful one, and a name
 * copied onto the row at deletion time is the only one left once it does not. The placeholder is
 * reached only by a row that is neither — which no row the panel writes should be.
 * @param backup The backup the row is drawn for.
 * @returns The account's short name, the name preserved at deletion, or a placeholder.
 */
const accountName = (backup: Backup): string => {
  const owner = accountsStore.accounts.find((account) => {
    return account.id === backup.accountId
  })
  return owner?.name ?? (backup.orphanedAccountUsername || t('common.emptyValue'))
}

/**
 * The size a row shows.
 *
 * A running or failed backup has no artifact, and its `sizeBytes` is zero — which formatted as
 * "0 B" would read as an archive that exists and is empty, rather than as no archive at all.
 * @param backup The backup the row is drawn for.
 * @returns The formatted size, or a placeholder when there is no artifact.
 */
const size = (backup: Backup): string => {
  return backup.status === 'completed' ? formatBytes(backup.sizeBytes) : t('common.emptyValue')
}

/**
 * The sentence a failed row shows above its code.
 *
 * The text is the backend's, already localized for the language `useApi` asked in — the panel holds
 * no name of its own for a server outcome (rules/vue.md). `null` when there is nothing to say: a
 * row that did not fail, or a panel older than the field, which sends the code alone.
 * @param backup The backup the row is drawn for.
 * @returns The localized failure name, or `null` when the row has none.
 */
const failureName = (backup: Backup): string | null => {
  // Read through a `typeof` rather than a plain length check: the declared type says `string`, but
  // the value comes off the wire, and a panel older than the field sends the key not at all. A
  // screen that threw on the row it exists to explain would be the worse failure.
  const name: unknown = backup.failureDisplayName
  return typeof name === 'string' && name.length > 0 ? name : null
}

/**
 * When the run ended, or a placeholder while it is still going.
 * @param backup The backup the row is drawn for.
 * @returns The formatted instant, or a placeholder when the run has not ended.
 */
const finished = (backup: Backup): string => {
  return backup.finishedAt === null
    ? t('common.emptyValue')
    : formatIsoTimestamp(backup.finishedAt, localeStore.current)
}

/**
 * Loads the two lists the screen needs: the backups themselves and the accounts one can be taken
 * of. Neither is invented here.
 * @returns Resolves once both requests have settled.
 */
const refresh = async (): Promise<void> => {
  await Promise.all([store.load(), accountsStore.load()])
}

/**
 * Sends a create request the form has already validated.
 * @param request The account the form collected.
 * @returns Resolves once the attempt has settled.
 */
const create = async (request: CreateBackupRequest): Promise<void> => {
  await store.create(request)
}

/**
 * Names the account a restore would replace, or `null` when the panel's account list does not name
 * it.
 *
 * `null` rather than the placeholder {@link accountName} returns: the dialog compares what is typed
 * against this value, and a translated "unknown account" string would be a confirmation nobody can
 * satisfy. Not knowing the name is the case where the client must stop judging and let the server
 * answer.
 * @param id The owning account's identity, as the backup row reports it.
 * @returns The account's system user name, or `null` when it is not known here.
 */
const accountUsername = (id: string): string | null => {
  const owner = accountsStore.accounts.find((account) => {
    return account.id === id
  })
  return owner?.name ?? null
}

/**
 * Opens the restore dialog on one row.
 * @param backup The backup the operator acted on.
 * @returns Nothing.
 */
const askRestore = (backup: Backup): void => {
  restoreTarget.value = backup
}

/**
 * Closes the restore dialog.
 * @returns Nothing.
 */
const closeRestore = (): void => {
  restoreTarget.value = null
}

/**
 * Starts a deletion on one row, which then waits for confirmation.
 * @param id The backup the operator acted on.
 * @returns Nothing.
 */
const ask = (id: string): void => {
  pendingId.value = id
}

/**
 * Abandons a pending deletion.
 * @returns Nothing.
 */
const cancel = (): void => {
  pendingId.value = ''
}

/**
 * Carries out the confirmed deletion.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  await store.remove(pendingId.value)

  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the operator the panel is working.
  pendingId.value = ''
}

onMounted(refresh)
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('backups.list.eyebrow')"
      :title="t('backups.list.heading')"
      :subtitle="t('backups.list.subtitle')"
      :note="t('backups.list.storageNote')"
    >
      <template #actions>
        <!-- The schedule is administrators-only and this link is not the gate: it is offered to
             whoever is looking, and the endpoint behind the page answers 403 to anyone who may not
             read it. A client-side check here would be a second copy of an authorization decision,
             and the copy that cannot be trusted (rules/vue.md). Both are ghost-weight links: the
             screen's primary action is the form below, and a pill up here would compete with it. -->
        <UiButton variant="secondary" :to="{ name: 'backup-schedule' }">{{ t('backups.list.scheduleLink') }}</UiButton>
        <UiButton variant="secondary" :to="{ name: 'backup-destinations' }">{{ t('backups.list.destinationsLink') }}</UiButton>
      </template>
    </UiPageHeading>

    <!-- The one thing that needs attention goes first. Warning, not error: the failure already
         happened and can be answered by taking another copy, which is the form just below. -->
    <UiAlert v-if="attentionMessage !== null && !store.loading" variant="warning">
      {{ attentionMessage }}
    </UiAlert>

    <UiAlert v-if="store.createErrorMessage !== null" variant="error">
      {{ store.createErrorMessage }}
    </UiAlert>

    <!-- The form draws its own panel, so it is not wrapped in a second one here. -->
    <BackupCreateForm
      :accounts="accountsStore.accounts"
      :submitting="store.creating"
      @submit="create"
    />

    <UiCard>
      <UiSectionHeading class="mb-4" :title="t('backups.list.sectionHeading')" />

      <UiSpinner v-if="store.loading" :label="t('backups.list.loading')" />

      <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

      <UiEmptyState
        v-else-if="isEmpty"
        :title="t('backups.list.emptyTitle')"
        :description="t('backups.list.emptyDescription')"
      >
        <template #icon><UiIcon name="archive" size="lg" /></template>
      </UiEmptyState>

      <UiTable v-else-if="store.backups.length > 0" :caption="t('backups.list.tableCaption')">
        <template #head>
          <UiTableRow>
            <UiTableHeaderCell>{{ t('backups.list.columns.account') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('backups.list.columns.status') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('backups.list.columns.kind') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('backups.list.columns.size') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('backups.list.columns.databases') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('backups.list.columns.startedAt') }}</UiTableHeaderCell>
            <UiTableHeaderCell>{{ t('backups.list.columns.finishedAt') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('common.actions') }}</UiTableHeaderCell>
          </UiTableRow>
        </template>
        <UiTableRow v-for="backup in store.backups" :key="backup.id">
          <!-- The cell holds the account's name and nothing else: a spec reads it as the cell's
               whole text, and a name is what the column is for. -->
          <UiTableCell mono class="font-semibold whitespace-nowrap">{{ accountName(backup) }}</UiTableCell>
          <UiTableCell>
            <BackupStatusBadge :status="backup.status" />
            <!-- Both halves of the failure, and neither instead of the other. The sentence is the
                 backend's own, already localized (rules/vue.md: the SPA owns no text for a server
                 outcome), and it is what a reader reads. The code below it is machine-stable across
                 languages, so it is what an operator quotes in a support ticket and greps a log for —
                 kept as visible, selectable text rather than hidden in a `title`, which no touch
                 device shows, no keyboard reaches and nothing can copy from. A panel older than the
                 display name sends the code alone, and then the code is the whole cell. -->
            <span
              v-if="failureName(backup) !== null"
              class="mt-1.5 block max-w-64 text-sm text-text-secondary wrap-anywhere"
            >
              {{ failureName(backup) }}
            </span>
            <span
              v-if="backup.failureCode.length > 0"
              class="mt-0.5 block font-mono text-xs whitespace-nowrap text-text-muted"
            >
              {{ t('backups.list.failureCode', { code: backup.failureCode }) }}
            </span>
          </UiTableCell>
          <UiTableCell tone="secondary">{{ t(`backups.kind.${backup.kind}`) }}</UiTableCell>
          <UiTableCell mono align="end" class="whitespace-nowrap">{{ size(backup) }}</UiTableCell>
          <UiTableCell mono tone="secondary" align="end">{{ backup.databaseCount }}</UiTableCell>
          <!-- A timestamp is context, never the thing being scanned for: the faintest text tier. -->
          <UiTableCell mono tone="muted" class="whitespace-nowrap">
            {{ formatIsoTimestamp(backup.startedAt, localeStore.current) }}
          </UiTableCell>
          <UiTableCell mono tone="muted" class="whitespace-nowrap">{{ finished(backup) }}</UiTableCell>
          <UiTableCell align="end">
            <!-- One trigger instead of a button per command, and a menu even on the rows that
                 hold only Delete: a running or failed row is offered no restore, so a table that
                 dropped the menu for a single command would change the shape of its last column
                 from row to row and an operator would have to re-find the control on every line.
                 `align="end"` because this is the last column — a menu aligned to the start would
                 open off the right edge. The trigger's name carries the row's account, because
                 "Actions" repeated down the column names nothing to a screen reader. -->
            <UiDropdown
              :label="t('common.actions')"
              :aria-label="t('backups.list.rowActions', { account: accountName(backup) })"
              align="end"
              variant="bare"
              :chevron="false"
            >
              <template #trigger>
                <UiIcon name="ellipsis" size="md" />
              </template>
              <!-- Offered only where an artifact exists. On a failed or still-running row the panel
                   would answer `BackupNotRestorable`, and a command whose only outcome is a refusal
                   teaches an operator that the screen is unreliable. -->
              <UiDropdownItem v-if="backup.status === 'completed'" @select="askRestore(backup)">
                {{ t('backups.restore.action') }}
              </UiDropdownItem>
              <UiDropdownItem destructive @select="ask(backup.id)">
                {{ t('common.delete') }}
              </UiDropdownItem>
            </UiDropdown>
          </UiTableCell>
        </UiTableRow>
      </UiTable>
    </UiCard>

    <!-- One dialog for the whole table rather than one per row: only one deletion can be awaiting
         an answer, and the menu that opened it is already gone by the time it appears. `:open` is
         bound to a value that really changes and the dialog is NOT wrapped in a `v-if`, so
         `UiModal`s open-watcher runs and its focus trap is live. -->
    <UiConfirm
      :open="pendingId.length > 0"
      :title="confirmationTitle"
      :question="t('backups.list.confirmDelete')"
      :confirm-label="t('backups.list.confirm')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('backups.list.working')"
      @close="cancel"
      @confirm="confirm"
    />

    <!-- `v-if` because `backup` is required and `restoreTarget` is nullable; the dialog is created
         with `open` already true, which `UiModal`s immediate open-watcher handles (focus enters,
         Escape reaches it, focus returns to the control that opened it). -->
    <BackupRestoreDialog
      v-if="restoreTarget !== null"
      :open="restoreTarget !== null"
      :backup="restoreTarget"
      :account-username="accountUsername(restoreTarget.accountId)"
      @close="closeRestore"
    />
  </section>
</template>
