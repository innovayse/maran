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
 * what is not before it will accept a typed confirmation. The note that used to stand here, that no
 * panel endpoint reached the agent's restore, was true when it was written and is false now: the
 * panel publishes `POST /api/v1/backups/{id}/restore`.
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
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
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
        account: accountName(pending.accountId),
        startedAt: formatIsoTimestamp(pending.startedAt, localeStore.current),
      })
})

/**
 * Names the account a backup belongs to, for a column that would otherwise print a GUID.
 * @param id The owning account's identity, as the backup row reports it.
 * @returns The account's own short name, or a placeholder when the accounts list has none.
 */
const accountName = (id: string): string => {
  const owner = accountsStore.accounts.find((account) => {
    return account.id === id
  })
  return owner?.name ?? t('common.emptyValue')
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
  <section class="w-full">
    <UiPageHeading
      class="mb-4"
      :title="t('backups.list.heading')"
      :subtitle="t('backups.list.subtitle')"
      :note="t('backups.list.storageNote')"
    >
      <template #actions>
        <!-- The schedule is administrators-only and this link is not the gate: it is offered to
             whoever is looking, and the endpoint behind the page answers 403 to anyone who may not
             read it. A client-side check here would be a second copy of an authorization decision,
             and the copy that cannot be trusted (rules/vue.md). -->
        <RouterLink
          class="rounded-lg border border-border-subtle px-3 py-2 text-base text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
          :to="{ name: 'backup-schedule' }"
          >{{ t('backups.list.scheduleLink') }}</RouterLink
        >
        <RouterLink
          class="rounded-lg border border-border-subtle px-3 py-2 text-base text-text-secondary transition-colors hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
          :to="{ name: 'backup-destinations' }"
          >{{ t('backups.list.destinationsLink') }}</RouterLink
        >
      </template>
    </UiPageHeading>

    <UiAlert v-if="store.createErrorMessage !== null" variant="error" class="mb-4">
      {{ store.createErrorMessage }}
    </UiAlert>

    <BackupCreateForm
      class="mb-6"
      :accounts="accountsStore.accounts"
      :submitting="store.creating"
      @submit="create"
    />

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
          <UiTableHeaderCell>{{ t('backups.list.columns.size') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('backups.list.columns.databases') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('backups.list.columns.startedAt') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('backups.list.columns.finishedAt') }}</UiTableHeaderCell>
          <UiTableHeaderCell align="end">{{ t('common.actions') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>
      <UiTableRow v-for="backup in store.backups" :key="backup.id">
        <UiTableCell class="font-medium">{{ accountName(backup.accountId) }}</UiTableCell>
        <UiTableCell>
          <BackupStatusBadge :status="backup.status" />
          <!-- Both halves of the failure, and neither instead of the other. The sentence is the
               backend's own, already localized (rules/vue.md: the SPA owns no text for a server
               outcome), and it is what a reader reads. The code below it is machine-stable across
               languages, so it is what an operator quotes in a support ticket and greps a log for —
               kept as visible, selectable text rather than hidden in a `title`, which no touch
               device shows, no keyboard reaches and nothing can copy from. A panel older than the
               display name sends the code alone, and then the code is the whole cell. -->
          <span v-if="failureName(backup) !== null" class="mt-1 block text-sm text-text-secondary">
            {{ failureName(backup) }}
          </span>
          <span
            v-if="backup.failureCode.length > 0"
            class="mt-0.5 block font-mono text-xs text-text-muted"
          >
            {{ t('backups.list.failureCode', { code: backup.failureCode }) }}
          </span>
        </UiTableCell>
        <UiTableCell class="text-text-secondary">{{ t(`backups.kind.${backup.kind}`) }}</UiTableCell>
        <UiTableCell class="font-mono text-text-secondary">{{ size(backup) }}</UiTableCell>
        <UiTableCell class="font-mono text-text-secondary">{{ backup.databaseCount }}</UiTableCell>
        <UiTableCell class="font-mono text-text-muted">
          {{ formatIsoTimestamp(backup.startedAt, localeStore.current) }}
        </UiTableCell>
        <UiTableCell class="font-mono text-text-muted">{{ finished(backup) }}</UiTableCell>
        <UiTableCell align="end">
          <div class="flex flex-wrap items-center justify-end gap-2">
            <!-- One trigger instead of a button per command, and a menu even on the rows that
                 hold only Delete: a running or failed row is offered no restore, so a table that
                 dropped the menu for a single command would change the shape of its last column
                 from row to row and an operator would have to re-find the control on every line.
                 `align="end"` because this is the last column — a menu aligned to the start would
                 open off the right edge. The trigger's name carries the row's account, because
                 "Actions" repeated down the column names nothing to a screen reader. -->
            <UiDropdown
              :label="t('common.actions')"
              :aria-label="t('backups.list.rowActions', { account: accountName(backup.accountId) })"
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
              <UiDropdownItem
                v-if="backup.status === 'completed'"
                @select="askRestore(backup)"
              >
                {{ t('backups.restore.action') }}
              </UiDropdownItem>
              <UiDropdownItem destructive @select="ask(backup.id)">
                {{ t('common.delete') }}
              </UiDropdownItem>
            </UiDropdown>
          </div>
        </UiTableCell>
      </UiTableRow>
    </UiTable>

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
