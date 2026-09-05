<script setup lang="ts">
/**
 * The panel's only icon primitive. Every glyph the SPA draws comes from
 * `lucide-vue-next` through this component; no component hand-writes an
 * `<svg>` any more (rules/vue.md: "Icons come from lucide-vue-next").
 *
 * It stays a wrapper rather than letting screens import lucide components
 * directly, so the size, the stroke weight and the decorative-by-default
 * treatment are decided once, every call site keeps passing a plain `name`
 * string, and swapping icon libraries again would touch this file alone.
 *
 * Always decorative: an icon here sits next to its own text label or inside a
 * control that carries its own accessible name, so it is hidden from assistive
 * technology rather than given a duplicate one (rules/vue.md: "a decorative
 * icon is declared rather than omitted"). A control whose ONLY content is an
 * icon must therefore carry an `aria-label` of its own.
 */
import {
  Activity,
  Bell,
  BrickWall,
  ChartLine,
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Clock,
  Copy,
  Database,
  Dices,
  Earth,
  Ellipsis,
  Eye,
  EyeOff,
  FolderKey,
  Globe,
  LayoutGrid,
  ListChecks,
  LogOut,
  Menu,
  Moon,
  Search,
  Server,
  ShieldCheck,
  Sparkle,
  Sun,
  User,
  Users,
  X,
  type LucideIcon,
} from 'lucide-vue-next'
import { computed, type ComputedRef } from 'vue'

/** Name of a panel icon; each maps to one lucide component in {@link ICONS}. */
export type UiIconName =
  /** Activity trace — the shell's own system status entry. */
  | 'pulse'
  /** Four tiles — a module the panel reported that this bundle has no glyph for. */
  | 'grid'
  /** People — the accounts module. */
  | 'users'
  /** Shield with a tick — the identity module's security screens. */
  | 'shieldCheck'
  /** Meridian sphere — the sites module. */
  | 'earth'
  /** Stacked discs — the databases module. */
  | 'database'
  /** Folder with a key — the SFTP module, whose logins open one directory and no other. */
  | 'folderKey'
  /** A course of bricks — the firewall module, whose screen is the host's packet filter. */
  | 'brickWall'
  /** Two sheets — copying a value, such as a one-time credential, to the clipboard. */
  | 'copy'
  /** Stacked racks — the server picker. */
  | 'server'
  /** Magnifier — the command/search trigger and the search field. */
  | 'search'
  /** Crescent — the theme toggle's dark state. */
  | 'moon'
  /** Rayed disc — the theme toggle's light state. */
  | 'sun'
  /** Meridians — the locale switcher. */
  | 'globe'
  /** Four-point star — an assistant-initiated action. */
  | 'sparkle'
  /** Notifications. */
  | 'bell'
  /** The signed-in account. */
  | 'user'
  /** Collapse the sidebar into the rail. */
  | 'chevronLeft'
  /** Expand the rail back into the sidebar. */
  | 'chevronRight'
  /** Opens a picker. */
  | 'chevronDown'
  /** Door with an outgoing arrow — signing out of the panel. */
  | 'logOut'
  /** Tick — a selected option, a ticked checkbox. */
  | 'check'
  /** Cross — dismissing a toast, a modal or a search term. */
  | 'x'
  /** Three rules — the header's sidebar toggle on narrow screens. */
  | 'menu'
  /** Three dots — the trigger of a row's actions menu, where a word would crowd the row. */
  | 'ellipsis'
  /** Open eye — the password value is currently revealed. */
  | 'eye'
  /** Struck-through eye — the password value is currently masked. */
  | 'eyeOff'
  /** A pair of dice — replacing a typed password with a randomly generated one. */
  | 'dices'
  /** A clock face — the cron module, whose screen is a list of things that happen at a time. */
  | 'clock'
  /** A ticked list — the tasks module's feed of background work. */
  | 'listChecks'
  /** A plotted line — the monitoring module's charts of what the server has been doing. */
  | 'chartLine'

/**
 * Every glyph the panel draws, mapped to its lucide component. The map is
 * explicit rather than derived from the name, so the set of icons in the SPA
 * is one readable list and an unknown name is a type error, not a blank box.
 */
const ICONS: Record<UiIconName, LucideIcon> = {
  pulse: Activity,
  grid: LayoutGrid,
  users: Users,
  shieldCheck: ShieldCheck,
  earth: Earth,
  database: Database,
  folderKey: FolderKey,
  brickWall: BrickWall,
  copy: Copy,
  server: Server,
  search: Search,
  moon: Moon,
  sun: Sun,
  globe: Globe,
  sparkle: Sparkle,
  bell: Bell,
  user: User,
  chevronLeft: ChevronLeft,
  chevronRight: ChevronRight,
  chevronDown: ChevronDown,
  logOut: LogOut,
  check: Check,
  x: X,
  menu: Menu,
  ellipsis: Ellipsis,
  eye: Eye,
  eyeOff: EyeOff,
  dices: Dices,
  clock: Clock,
  listChecks: ListChecks,
  chartLine: ChartLine,
}

/**
 * The named size steps an icon may be drawn at. A glyph is chosen by role, not
 * by pixel count: `sm` for a mark inside a dense control, `md` beside body
 * text, `lg` for a glyph that stands on its own.
 */
export type UiIconSize = 'sm' | 'md' | 'lg'

/**
 * Edge length in CSS pixels for each step, paired with the stroke weight that
 * reads correctly at it. The weight is NOT constant across the steps: one
 * absolute stroke looks heavy on a small glyph and spidery on a large one, so
 * it eases down as the glyph grows.
 */
const SIZES: Record<UiIconSize, { edge: number; strokeWidth: number }> = {
  sm: { edge: 14, strokeWidth: 2 },
  md: { edge: 18, strokeWidth: 1.8 },
  lg: { edge: 22, strokeWidth: 1.6 },
}

/** Props accepted by {@link UiIcon}. */
const props = withDefaults(
  defineProps<{
    /** Which glyph to draw. */
    name: UiIconName
    /**
     * Which step of the icon scale to draw at. There is deliberately no
     * numeric escape hatch: a free pixel prop is what let six different sizes
     * accumulate across the panel, each chosen by whoever wrote the call site.
     * A glyph that genuinely does not fit any step is a reason to change a
     * step here, for every screen at once.
     */
    size?: UiIconSize
  }>(),
  { size: 'md' },
)

/** The pixel edge and stroke weight of the requested step. */
const step: ComputedRef<{ edge: number; strokeWidth: number }> = computed(() => {
  return SIZES[props.size]
})

/** The lucide component for the requested {@link UiIconName}. */
const glyph: ComputedRef<LucideIcon> = computed(() => {
  return ICONS[props.name]
})
</script>

<template>
  <component
    :is="glyph"
    :size="step.edge"
    :stroke-width="step.strokeWidth"
    aria-hidden="true"
    focusable="false"
    class="shrink-0"
  />
</template>
