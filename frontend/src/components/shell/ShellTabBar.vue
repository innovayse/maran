<script setup lang="ts">
/**
 * The phone shell's bottom tab bar: five destinations, four of them navigation entries and the
 * fifth a "More" button that opens the drawer, which holds everything else.
 *
 * **Which four.** The common destinations first, in a fixed order per role — for an operator the
 * landing screen, accounts, sites and databases; for a customer the landing screen, sites, mail and
 * their own account (issue #312) — taken from the entries the navigation actually gave this person.
 * A preferred entry that is missing (a module not installed) is replaced by the next entry in
 * drawing order rather than leaving a gap. A preferred entry whose page is still planned is drawn in
 * its place, disabled, with the "coming soon" hint (the customer's Mail until #177): the bar shows
 * the shape of the product the same way the sidebar does. A fill-up entry is never a planned one —
 * a tab chosen only to fill the bar must lead somewhere. The orders are a presentation decision
 * about which screens are used most; the entries, their targets and their states still come from
 * the navigation tree.
 *
 * **Every label stays whole, in every language, from 320px up.** A tab is about a fifth of the
 * bar (55px at 320px), and a label cut off with an ellipsis ("Նորությու…") or broken inside a word
 * ("Database" over "s", "Գլխավո" over "ր") is a destination the person has to guess; reflow at
 * 320px (WCAG 1.4.10) is judged by exactly this. So the bar uses short labels from the locale
 * files, set on one line in `text-xs`, and the columns are `minmax(min-content, 1fr)`: five equal
 * columns while every word fits its column, and a column that grows to its longest word (taking
 * the room from its shorter neighbours, "Sites" and "More" have plenty to spare) when one does
 * not. No label is allowed to break anywhere, so there is no `wrap-anywhere` here; a label of
 * several words still wraps between them. An entry with no short label uses the catalogue's own
 * name, which is the one place a long label could appear: its column grows to its longest word
 * and the bar grows a line instead of losing letters.
 *
 * The current tab is `--act` text on the accent wash over a panel (5.15:1 dark); it is never
 * drawn on `--s3`, where that pair is 4.40:1 and fails. `aria-current="page"` marks the exact
 * page for assistive technology, the same as in the sidebar.
 *
 * The "More" button is the second opener of the drawer; `aria-expanded` follows the same
 * state as the hamburger's. Its accessible name is its visible word, so the two openers have
 * different names and a script can tell them apart.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiNavPlanned from '../ui/UiNavPlanned.vue'
import { useNavigationRole } from '../../composables/useNavigationRole'
import type { RouteLocationRaw } from 'vue-router'
import type { NavigationEntry } from '../../types/navigation'

/** One preferred tab: the entry key (the tree's own) and the locale key of its SHORT label. */
interface PreferredTab {
  /** Key of the navigation entry. */
  key: string
  /** Locale key of the short label drawn under the icon. */
  labelKey: string
}

/** The tabs an operator's bar prefers, in order. */
const OPERATOR_PREFERRED: readonly PreferredTab[] = [
  { key: 'dashboard', labelKey: 'app.shell.tabs.home' },
  { key: 'accounts', labelKey: 'app.shell.tabs.accounts' },
  { key: 'sites', labelKey: 'app.shell.tabs.sites' },
  { key: 'databases', labelKey: 'app.shell.tabs.databases' },
]

/** The tabs a customer's bar prefers, in order (issue #312). */
const CUSTOMER_PREFERRED: readonly PreferredTab[] = [
  { key: 'dashboard', labelKey: 'app.shell.tabs.home' },
  { key: 'sites', labelKey: 'app.shell.tabs.sites' },
  { key: 'mail', labelKey: 'app.shell.tabs.mail' },
  { key: 'account', labelKey: 'app.shell.tabs.account' },
]

/** How many destinations the bar holds besides "More". */
const DESTINATIONS = 4

/** One drawn tab: a destination, or a preferred page that is still planned and drawn disabled. */
type Tab =
  | {
      /** The tab leads somewhere. */
      kind: 'open'
      /** The navigation entry this tab opens. */
      entry: NavigationEntry
      /** Where the tab leads: the entry's destination. */
      target: RouteLocationRaw
      /** Already-translated label, short enough to stay whole at 390px. */
      label: string
    }
  | {
      /** The tab's page is not built yet. */
      kind: 'planned'
      /** The navigation entry the tab stands for. */
      entry: NavigationEntry
      /** Already-translated label, short enough to stay whole at 390px. */
      label: string
    }

/** Props accepted by {@link ShellTabBar}. */
const props = defineProps<{
  /** Navigation entries the shell is showing, exactly as the sidebar renders them. */
  entries: readonly NavigationEntry[]
  /** Whether the navigation drawer is open, reported as the More button's `aria-expanded`. */
  navigationOpen: boolean
}>()

/** Events emitted by {@link ShellTabBar}. */
const emit = defineEmits<{
  /** The user pressed "More"; the layout owns the drawer. */
  (e: 'openNavigation'): void
}>()

const { t } = useI18n()
const role = useNavigationRole()

/**
 * The four destinations: preferred entries that exist, then the remaining entries in
 * catalogue order until the bar is full.
 */
const tabs: ComputedRef<Tab[]> = computed(() => {
  const chosen: Tab[] = []
  const used = new Set<string>()

  const preferredTabs = role.value === 'customer' ? CUSTOMER_PREFERRED : OPERATOR_PREFERRED
  for (const preferred of preferredTabs) {
    const entry = props.entries.find((candidate) => {
      return candidate.key === preferred.key
    })
    if (entry === undefined) {
      continue
    }
    const label = t(preferred.labelKey)
    chosen.push(
      entry.state.kind === 'planned'
        ? { kind: 'planned', entry, label }
        : { kind: 'open', entry, target: entry.state.target, label },
    )
    used.add(entry.key)
  }

  for (const entry of props.entries) {
    // A tab is a destination: an entry that leads nowhere yet is left to the drawer.
    if (entry.state.kind === 'planned') {
      continue
    }
    if (chosen.length >= DESTINATIONS) {
      break
    }
    if (!used.has(entry.key)) {
      chosen.push({
        kind: 'open',
        entry,
        target: entry.state.target,
        label: entry.labelKey === null ? (entry.label ?? '') : t(entry.labelKey),
      })
    }
  }

  return chosen.slice(0, DESTINATIONS)
})

/**
 * Asks the shell to open the drawer.
 * @returns Nothing; the event is emitted synchronously.
 */
const openNavigation = (): void => {
  emit('openNavigation')
}
</script>

<template>
  <nav
    class="grid h-16 flex-none grid-cols-[repeat(5,minmax(min-content,1fr))] rounded-panel border border-border-subtle bg-surface-1 px-1"
    :aria-label="t('app.shell.tabBarLabel')"
  >
    <template v-for="tab in tabs" :key="tab.entry.key">
      <UiNavPlanned v-if="tab.kind === 'planned'" :hint="t('app.nav.plannedHint')" tab>
        <UiIcon :name="tab.entry.icon" size="lg" />
        <span class="max-w-full">{{ tab.label }}</span>
      </UiNavPlanned>
      <RouterLink
        v-else
        :to="tab.target"
        class="m-0.5 flex min-w-0 flex-col items-center justify-center gap-1 rounded-control px-0 text-center text-xs leading-tight font-semibold text-text-secondary transition-colors focus-visible:shadow-focus focus-visible:outline-none [&.router-link-active]:bg-accent-soft [&.router-link-active]:text-accent-text"
      >
        <UiIcon :name="tab.entry.icon" size="lg" />
        <span class="max-w-full">{{ tab.label }}</span>
      </RouterLink>
    </template>

    <UiButton
      variant="ghost"
      size="tab"
      class="m-0.5"
      :aria-expanded="navigationOpen"
      :aria-controls="navigationOpen ? 'shell-drawer' : undefined"
      @click="openNavigation"
    >
      <UiIcon name="ellipsis" size="lg" />
      <span class="max-w-full">{{ t('app.shell.tabs.more') }}</span>
    </UiButton>
  </nav>
</template>
