import { createRequire } from 'node:module'
import { readFileSync, mkdirSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

/**
 * Renders every raster icon from the one master drawing.
 *
 * Usage, from anywhere in the repository:
 *
 *     node docs/brand/render-icons.mjs frontend
 *     node docs/brand/render-icons.mjs website
 *
 * The rasters are checked in, so this is not part of any build. It exists so that a change to the
 * mark is applied by REGENERATING them rather than by somebody exporting ten PNGs by hand and
 * getting one of them subtly wrong — the failure nobody notices, because a wrong favicon still
 * looks like a favicon.
 *
 * It reads `<target>/public/favicon.svg` rather than the master in this directory, deliberately.
 * The favicon is the master with the Gaussian bloom removed, and it is what the browser actually
 * ships; rendering the full master would produce icons that do not match the SVG served beside
 * them. If those two ever need to differ further, this is the line that decides it.
 *
 * Chrome does the rasterising, through Playwright, which the frontend already depends on for its
 * end-to-end suite — so this adds no dependency of its own.
 */

const HERE = dirname(fileURLToPath(import.meta.url))
const REPO = resolve(HERE, '../..')
const TARGET = process.argv[2] ?? 'frontend'
const OUT = resolve(REPO, TARGET, 'public')

const require = createRequire(resolve(REPO, 'frontend/package.json'))
const { chromium } = require('playwright')

const svg = readFileSync(resolve(OUT, 'favicon.svg'), 'utf8')

/**
 * Every raster the product ships, and how much of its canvas the mark is given.
 *
 * A transparent icon takes the whole canvas, because the drawing already carries its own margin —
 * its ink spans 5.5..26.5 of a 32-unit grid. The two masked formats do not, and for different
 * reasons: iOS crops and rounds an apple-touch icon, while a maskable icon is guaranteed only the
 * central 80% circle. Each is pulled in far enough that the crop cannot bite the stone.
 */
const JOBS = [
  { name: 'icon-16.png', size: 16, scale: 1, bg: null },
  { name: 'icon-32.png', size: 32, scale: 1, bg: null },
  { name: 'icon-48.png', size: 48, scale: 1, bg: null },
  { name: 'icon-64.png', size: 64, scale: 1, bg: null },
  { name: 'icon-128.png', size: 128, scale: 1, bg: null },
  { name: 'icon-192.png', size: 192, scale: 1, bg: null },
  { name: 'icon-256.png', size: 256, scale: 1, bg: null },
  { name: 'icon-512.png', size: 512, scale: 1, bg: null },
  { name: 'apple-touch-icon.png', size: 180, scale: 0.74, bg: '#0a0b0d' },
  { name: 'maskable-512.png', size: 512, scale: 0.54, bg: '#0a0b0d' },
]

const browser = await chromium.launch({ channel: 'chrome' })
const page = await browser.newPage()
mkdirSync(OUT, { recursive: true })

for (const job of JOBS) {
  const inner = Math.round(job.size * job.scale)
  const sized = svg.replace('<svg ', `<svg width="${inner}" height="${inner}" `)

  await page.setViewportSize({ width: job.size, height: job.size })
  await page.setContent(
    `<!doctype html><html><body style="margin:0;width:${job.size}px;height:${job.size}px;`
    + `display:flex;align-items:center;justify-content:center;`
    + `background:${job.bg ?? 'transparent'}">`
    + `<div style="width:${inner}px;height:${inner}px">${sized}</div>`
    + `</body></html>`,
  )
  await page.screenshot({
    path: resolve(OUT, job.name),
    omitBackground: job.bg === null,
    clip: { x: 0, y: 0, width: job.size, height: job.size },
  })

  console.log(`${job.name.padEnd(22)} ${String(job.size).padStart(3)}px  mark ${inner}px  ${job.bg ?? 'transparent'}`)
}

await browser.close()

console.log(`\nwritten to ${OUT}`)
console.log('favicon.ico is built separately — see docs/brand/README.md')
