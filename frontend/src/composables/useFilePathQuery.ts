import { computed, type ComputedRef } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw, type RouteLocationRaw } from 'vue-router'
import { parentPath, pathTrail } from '../utils/filePath'
import type { AccountChoice } from './useAccountChoice'
import { useAccountStart } from './useAccountStart'
import type { ListFilterSchema, ListFilterSpec, ServerTable } from './useServerTable'

/** The value of the `hidden` filter that shows names beginning with a dot. */
const HIDDEN_SHOWN = '1'

/**
 * The typed filters a file browser's `useServerTable` takes: which account is looked at, which
 * folder, and whether dot-names are shown. They live in the URL like every list's filters, so a
 * folder can be bookmarked, reloaded and walked back through.
 *
 * `path` accepts any text on purpose: a value typed or edited into the address is sent as typed and
 * the server's answer is what the person reads. The panel makes no claim here about what stays
 * inside the home (rules/security.md item 2); the agent alone decides that.
 * @param account The account filter's spec (`useAccountFilter().spec`), which a customer's list ignores.
 * @returns The schema to hand `useServerTable`.
 */
export const fileBrowserFilters = (account: ListFilterSpec): ListFilterSchema => {
  return {
    account,
    path: {},
    hidden: {
      /**
       * Honours only the one value the toggle writes, so a hand-edited link never reaches the server.
       * @param value The `?hidden=` value.
       * @returns True for the toggle's own value.
       */
      accepts: (value: string): boolean => {
        return value === HIDDEN_SHOWN
      },
    },
  }
}

/** One part of the path bar: a folder the person can jump to. */
export interface FileTrailPart {
  /** The folder's own name. */
  name: string
  /** The folder's path relative to the home. */
  path: string
}

/** What {@link useFilePathQuery} hands the file manager page. */
export interface FilePathQuery {
  /**
   * The account whose files are on screen: a customer's own, from their token, else the one the URL
   * names. Empty while an administrator has not chosen one.
   */
  accountId: ComputedRef<string>
  /** The folder on screen, relative to the home; empty for the home. */
  path: ComputedRef<string>
  /** Whether names beginning with a dot are shown. */
  showHidden: ComputedRef<boolean>
  /** The ancestors of the folder, outermost first, ending with the folder itself; empty at the home. */
  trail: ComputedRef<FileTrailPart[]>
  /** The label the account picker shows for the chosen account, which may not be on its page of matches. */
  selectedAccountLabel: ComputedRef<string | undefined>
  /**
   * Where a link to a folder goes: the same screen with the same account, sort, page size and
   * dot-name toggle, back on page 1 with the search emptied, because a name filter typed for one
   * folder means nothing in another. Opening it is a navigation (it joins the history), so Back
   * returns to the folder the person came from.
   * @param folder The folder, relative to the home; empty for the home.
   * @returns The location to link or push.
   */
  target: (folder: string) => RouteLocationRaw
  /**
   * Opens a folder by navigating to {@link FilePathQuery.target}.
   * @param folder The folder, relative to the home.
   * @returns Resolves once the navigation has settled.
   */
  openFolder: (folder: string) => Promise<void>
  /**
   * Opens the folder that holds the current one; does nothing at the home.
   * @returns Resolves once the URL has changed.
   */
  goUp: () => Promise<void>
  /**
   * Shows or hides names beginning with a dot.
   * @param shown Whether they are shown.
   * @returns Resolves once the URL has changed.
   */
  setShowHidden: (shown: boolean) => Promise<void>
  /**
   * Looks at another account's home.
   * @param id The account chosen.
   * @returns Resolves once the URL has changed.
   */
  changeAccount: (id: string) => Promise<void>
  /**
   * Points an administrator at an account when the URL names none: the first one the panel lists,
   * because a screen that opens on an empty picker looks broken. Nothing is invented in it, the
   * account came from the panel. A customer is never offered a choice and nothing happens.
   * @returns Resolves once the account has been written to the URL.
   */
  start: () => Promise<void>
}

/**
 * Reads and changes where the file browser is looking: the account, the folder and the dot-name
 * toggle, all of which live in the list's URL (`useServerTable`'s filters).
 *
 * Presentation only: every endpoint checks the account id and the path it is given and answers 404
 * for another tenant's account, whatever this composable decided.
 * @param table The browser's server table, built with {@link fileBrowserFilters}.
 * @param choice The page's account choice; passed in because the table's loader needs the fixed
 * account before this composable can exist.
 * @returns The three values and the actions that change them.
 */
export const useFilePathQuery = (table: ServerTable, choice: AccountChoice): FilePathQuery => {
  const route = useRoute()
  const router = useRouter()
  const { fixedAccountId } = choice

  const accountId: ComputedRef<string> = computed(() => {
    return fixedAccountId.value ?? table.filters.value.account
  })

  const path: ComputedRef<string> = computed(() => {
    return table.filters.value.path
  })

  const showHidden: ComputedRef<boolean> = computed(() => {
    return table.filters.value.hidden === HIDDEN_SHOWN
  })

  const trail: ComputedRef<FileTrailPart[]> = computed(() => {
    return pathTrail(path.value)
  })

  const { selectedAccountLabel, start } = useAccountStart(table, choice, accountId)

  /**
   * Writes the given filters over the current ones, leaving the others as they are.
   * @param changes The filters to set; an empty value clears one.
   * @returns Resolves once the URL has changed.
   */
  const write = async (changes: Record<string, string>): Promise<void> => {
    await table.setFilters({ ...table.filters.value, ...changes })
  }

  /**
   * Builds the location of a folder on this screen.
   * @param folder The folder, relative to the home.
   * @returns The current query without page and search, and with the folder (absent for the home).
   */
  const target = (folder: string): RouteLocationRaw => {
    const query: LocationQueryRaw = { ...route.query, path: folder === '' ? undefined : folder }
    // A folder is read from its first page, and a name filter typed for another folder is dropped.
    delete query.page
    delete query.search
    return { query }
  }

  /**
   * Opens a folder by navigating to its location.
   * @param folder The folder, relative to the home.
   * @returns Resolves once the navigation has settled.
   */
  const openFolder = async (folder: string): Promise<void> => {
    await router.push(target(folder))
  }

  /**
   * Opens the folder above the current one.
   * @returns Resolves once the navigation has settled; nothing happens at the home.
   */
  const goUp = async (): Promise<void> => {
    if (path.value !== '') {
      await openFolder(parentPath(path.value))
    }
  }

  /**
   * Shows or hides names beginning with a dot.
   * @param shown Whether they are shown.
   * @returns Resolves once the URL has changed.
   */
  const setShowHidden = async (shown: boolean): Promise<void> => {
    await write({ hidden: shown ? HIDDEN_SHOWN : '' })
  }

  /**
   * Looks at another account's home, starting at its root.
   * @param id The account chosen.
   * @returns Resolves once the URL has changed.
   */
  const changeAccount = async (id: string): Promise<void> => {
    table.search.value = ''
    // Another account's home starts at its root: the old folder may not exist there.
    await write({ account: id, path: '' })
  }

  return {
    accountId,
    path,
    showHidden,
    trail,
    selectedAccountLabel,
    target,
    openFolder,
    goUp,
    setShowHidden,
    changeAccount,
    start,
  }
}
