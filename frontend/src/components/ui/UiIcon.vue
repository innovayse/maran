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
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Eye,
  EyeOff,
  Globe,
  LayoutGrid,
  LogOut,
  Menu,
  Moon,
  Search,
  Server,
  Sparkle,
  Sun,
  User,
  X,
  type LucideIcon,
} from 'lucide-vue-next'
import { computed, type ComputedRef } from 'vue'

/** Name of a panel icon; each maps to one lucide component in {@link ICONS}. */
export type UiIconName =
  /** Activity trace — the shell's own system status entry. */
  | 'pulse'
  /** Four tiles — a module reported by the panel's catalogue. */
  | 'grid'
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
  /** Open eye — the password value is currently revealed. */
  | 'eye'
  /** Struck-through eye — the password value is currently masked. */
  | 'eyeOff'

/**
 * Every glyph the panel draws, mapped to its lucide component. The map is
 * explicit rather than derived from the name, so the set of icons in the SPA
 * is one readable list and an unknown name is a type error, not a blank box.
 */
const ICONS: Record<UiIconName, LucideIcon> = {
  pulse: Activity,
  grid: LayoutGrid,
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
  eye: Eye,
  eyeOff: EyeOff,
}

/** Props accepted by {@link UiIcon}. */
const props = withDefaults(
  defineProps<{
    /** Which glyph to draw. */
    name: UiIconName
    /** Edge length in CSS pixels; the design draws shell icons at 15 or 16. */
    size?: number
    /** Stroke weight; the shell's own line weight, lighter than lucide's default of 2. */
    strokeWidth?: number
  }>(),
  { size: 15, strokeWidth: 1.7 },
)

/** The lucide component for the requested {@link UiIconName}. */
const glyph: ComputedRef<LucideIcon> = computed(() => {
  return ICONS[props.name]
})
</script>

<template>
  <component
    :is="glyph"
    :size="props.size"
    :stroke-width="props.strokeWidth"
    aria-hidden="true"
    focusable="false"
    class="shrink-0"
  />
</template>
