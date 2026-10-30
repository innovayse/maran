import type { Backup } from '../types/backup'

/**
 * Names the account a backup belongs to, for a place that would otherwise print a GUID.
 *
 * The live account's name, as the panel put it on the row (`ownerName`), is asked first, and the
 * backup's own `orphanedAccountUsername` second. That order matters: while an account exists its
 * current name is the truthful one, and a name copied onto the row at deletion time is the only one
 * left once it does not. The placeholder is reached only by a row that is neither — which no row
 * the panel writes should be.
 * @param backup The backup to name the account of.
 * @param placeholder The already-translated text shown when the row names no account.
 * @returns The account's short name, the name preserved at deletion, or the placeholder.
 */
export const backupAccountName = (backup: Backup, placeholder: string): string => {
  if (backup.ownerName !== '') {
    return backup.ownerName
  }
  return backup.orphanedAccountUsername === '' ? placeholder : backup.orphanedAccountUsername
}
