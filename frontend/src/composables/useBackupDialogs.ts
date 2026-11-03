import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useBackupsStore } from '../stores/backups'
import { useLocaleStore } from '../stores/locale'
import { backupAccountName } from '../utils/backupAccountName'
import { formatIsoTimestamp } from '../utils/formatIsoTimestamp'
import type { Backup } from '../types/backup'

/** The dialog open over the history: a deletion awaiting its answer, or a restore. */
export type BackupDialog =
  | {
      /** The backup is about to be deleted. */
      kind: 'remove'
      /** The backup the confirmation is for; its row supplies the wording. */
      backupId: string
    }
  | {
      /** The backup is about to be restored. */
      kind: 'restore'
      /** The backup, whole, because the dialog states what it holds. */
      backup: Backup
    }

/** What {@link useBackupDialogs} hands the backups screen. */
export interface BackupDialogs {
  /**
   * The dialog open over the list, or `null` when none is. A deletion names the backup by id (the
   * confirmation reads its row); a restore holds the backup whole, as the dialog states what it holds.
   * The two are modals, so one value says which.
   */
  dialog: Ref<BackupDialog | null>
  /** The confirmation's accessible name, naming the copy that would be destroyed. */
  confirmationTitle: ComputedRef<string>
  /**
   * Names the account a restore would replace, or `null` when the panel did not name it on the row.
   * @param backup The backup a restore is asked of.
   * @returns The live account's system user name, or `null` when it is not known here.
   */
  accountUsername: (backup: Backup) => string | null
  /**
   * Opens the restore dialog on one row.
   * @param backup The backup the operator acted on.
   * @returns Nothing.
   */
  askRestore: (backup: Backup) => void
  /**
   * Closes the restore dialog.
   * @returns Nothing.
   */
  closeRestore: () => void
  /**
   * Starts a deletion on one row, which then waits for confirmation.
   * @param id The backup the operator acted on.
   * @returns Nothing.
   */
  ask: (id: string) => void
  /**
   * Abandons a pending deletion.
   * @returns Nothing.
   */
  cancel: () => void
  /**
   * Carries out the confirmed deletion, then reads the history again.
   * @returns Resolves once the request and the reread have settled.
   */
  confirm: () => Promise<void>
}

/**
 * The two dialogs over the backup history: the deletion confirmation and the restore dialog.
 *
 * **The two actions are not shaped alike.** Delete destroys a copy and is confirmed by `UiConfirm`.
 * Restore destroys the ACCOUNT, so it opens `BackupRestoreDialog`, which states what is replaced
 * before it accepts a typed confirmation. One value ({@link BackupDialog}) says which is open.
 * @param refresh Reads the history again after a deletion, because where a row falls is the
 * server's to decide.
 * @returns The open dialog and the actions that move it.
 */
export const useBackupDialogs = (refresh: () => Promise<void>): BackupDialogs => {
  const { t } = useI18n()
  const store = useBackupsStore()
  const localeStore = useLocaleStore()

  const dialog: Ref<BackupDialog | null> = ref(null)

  /** The backup the confirmation is open for, or `null` when none is. */
  const pendingBackup: ComputedRef<Backup | null> = computed(() => {
    const open = dialog.value
    if (open === null || open.kind !== 'remove') {
      return null
    }
    return store.backups.find((backup) => {
      return backup.id === open.backupId
    }) ?? null
  })

  /**
   * The confirmation's accessible name, naming the copy that would be destroyed.
   */
  const confirmationTitle: ComputedRef<string> = computed(() => {
    const pending = pendingBackup.value
    return pending === null
      ? ''
      : t('backups.list.confirmDeleteTitle', {
          account: backupAccountName(pending, t('common.emptyValue')),
          startedAt: formatIsoTimestamp(pending.startedAt, localeStore.current),
        })
  })

  /**
   * Names the account a restore would replace, or `null` when the panel did not name it on the row.
   *
   * `null` rather than a placeholder: the dialog compares what is typed against it, and a translated
   * "unknown account" would be a confirmation nobody can satisfy; the server judges instead.
   * @param backup The backup a restore is asked of.
   * @returns The live account's system user name, or `null` when it is not known here.
   */
  const accountUsername = (backup: Backup): string | null => {
    return backup.ownerName === '' ? null : backup.ownerName
  }

  /**
   * Opens the restore dialog on one row.
   * @param backup The backup the operator acted on.
   * @returns Nothing.
   */
  const askRestore = (backup: Backup): void => {
    dialog.value = { kind: 'restore', backup }
  }

  /**
   * Closes the restore dialog.
   * @returns Nothing.
   */
  const closeRestore = (): void => {
    dialog.value = null
  }

  /**
   * Starts a deletion on one row, which then waits for confirmation.
   * @param id The backup the operator acted on.
   * @returns Nothing.
   */
  const ask = (id: string): void => {
    dialog.value = { kind: 'remove', backupId: id }
  }

  /**
   * Abandons a pending deletion.
   * @returns Nothing.
   */
  const cancel = (): void => {
    dialog.value = null
  }

  /**
   * Carries out the confirmed deletion, then reads the history again.
   * @returns Resolves once the request and the reread have settled.
   */
  const confirm = async (): Promise<void> => {
    const open = dialog.value
    if (open === null || open.kind !== 'remove') {
      return
    }
    const removed = await store.remove(open.backupId)

    // Closed after the request settles rather than before it is sent: the dialog holds the
    // spinner that tells the operator the panel is working.
    dialog.value = null
    if (removed) {
      await refresh()
    }
  }

  return { dialog, confirmationTitle, accountUsername, askRestore, closeRestore, ask, cancel, confirm }
}
