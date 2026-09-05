import { createApp } from 'vue'
import UiChart, { type UiChartPoint } from '@/components/ui/UiChart.vue'
import { createAppI18n } from '@/i18n'
import '@/assets/css/main.css'

// Fixed UTC instant the stubbed buckets are spaced from, so the rendered chart and its
// formatted timestamps never depend on the machine's clock or timezone.
const BASE_AT = Date.UTC(2026, 0, 1, 0, 0, 0)
const HOUR_MS = 60 * 60 * 1000

// Five hourly buckets. Point index 2 (value 72) sits exactly at the series' horizontal
// midpoint, so hovering the chart's own bounding-box centre deterministically lands on it.
const POPULATED_SERIES: UiChartPoint[] = [
  { at: BASE_AT, value: 10 },
  { at: BASE_AT + HOUR_MS, value: 45 },
  { at: BASE_AT + HOUR_MS * 2, value: 72 },
  { at: BASE_AT + HOUR_MS * 3, value: 30 },
  { at: BASE_AT + HOUR_MS * 4, value: 60 },
]

const SCENARIOS: Record<string, UiChartPoint[]> = {
  populated: POPULATED_SERIES,
  empty: [],
  single: [{ at: BASE_AT, value: 42 }],
}

const scenario = new URLSearchParams(window.location.search).get('scenario') ?? 'populated'
const series = SCENARIOS[scenario] ?? POPULATED_SERIES

const app = createApp(UiChart, {
  series,
  label: 'CPU',
  unit: 'custom-unit',
  // Two decimals, unlike the component's own one-decimal default: proves the readout and the
  // table go through THIS formatter rather than the built-in fallback.
  formatValue: (value: number): string => {
    return value.toFixed(2)
  },
})

app.use(createAppI18n())
app.mount('#app')
