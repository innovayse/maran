<script setup lang="ts">
/**
 * The phone shell's bottom tab bar: five destinations, four of them navigation entries and the
 * fifth a "More" button that opens the drawer, which holds everything else.
 *
 * **Which four.** The common destinations first, in a fixed order — the landing screen, then
 * accounts, sites and databases — taken from the entries the panel actually gave this person.
 * A customer has no "accounts" entry, so the bar fills up with the next entries in catalogue
 * order instead of showing a gap or a link that goes nowhere. The order of {@link PREFERRED}
 * is a presentation decision about which screens are used most; the entries themselves, their
 * targets and whether they are locked still come from the catalogue.
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
import type { NavigationEntry } from '../../types/navigation'

/**
 * Entry keys the bar prefers, in order, with the locale key of each one's SHORT label. The keys
 * are the entries' own (`system-status`, or the module name for a module-backed entry).
 */
const PREFERRED: readonly { key: string; labelKey: string }[] = [
  { key: 'system-status', labelKey: 'app.shell.tabs.home' },
  { key: 'accounts', labelKey: 'app.shell.tabs.accounts' },
  { key: 'sites', labelKey: 'app.shell.tabs.sites' },
  { key: 'databases', labelKey: 'app.shell.tabs.databases' },
]

/** How many destinations the bar holds besides "More". */
const DESTINATIONS = 4

/** One drawn tab: the entry it leads to and the text under its icon. */
interface Tab {
  /** The navigation entry this tab opens. */
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

/**
 * The four destinations: preferred entries that exist, then the remaining entries in
 * catalogue order until the bar is full.
 */
const tabs: ComputedRef<Tab[]> = computed(() => {
  const chosen: Tab[] = []
  const used = new Set<string>()

  for (const preferred of PREFERRED) {
    const entry = props.entries.find((candidate) => {
      return candidate.key === preferred.key
    })
    if (entry !== undefined) {
      chosen.push({ entry, label: t(preferred.labelKey) })
      used.add(entry.key)
    }
  }

  for (const entry of props.entries) {
    if (chosen.length >= DESTINATIONS) {
      break
    }
    if (!used.has(entry.key)) {
      chosen.push({ entry, label: entry.labelKey === null ? (entry.label ?? '') : t(entry.labelKey) })
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
    <RouterLink
      v-for="tab in tabs"
      :key="tab.entry.key"
      :to="tab.entry.target"
      class="m-0.5 flex min-w-0 flex-col items-center justify-center gap-1 rounded-control px-0 text-center text-xs leading-tight font-semibold text-text-secondary transition-colors focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none [&.router-link-active]:bg-accent-soft [&.router-link-active]:text-accent-text"
    >
      <UiIcon :name="tab.entry.icon" size="lg" />
      <span class="max-w-full">{{ tab.label }}</span>
    </RouterLink>

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
