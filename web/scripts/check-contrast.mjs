// Reads the two token blocks of src/index.css (dark in :root, light in :root[data-theme='light']) and checks WCAG contrast.
// Also fails when a colour literal sits outside those blocks, or when public/theme-init.js disagrees with --bg.
// Run: npm run check:contrast. The pairs here are the ones listed in STYLE.md.
import { readFileSync } from 'node:fs'

const root = new URL('../', import.meta.url)
const css = readFileSync(new URL('src/index.css', root), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

function block(selector) {
  const start = css.indexOf(selector + ' {')
  if (start < 0) throw new Error(`token block not found: ${selector}`)
  const end = css.indexOf('\n}', start)
  return { text: css.slice(start, end + 2), start, end: end + 2 }
}
const darkBlock = block(':root')
const lightBlock = block(":root[data-theme='light']")

function tokens(text, base = {}) {
  const t = { ...base }
  for (const m of text.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) t[m[1]] = m[2].trim()
  return t
}
const dark = tokens(darkBlock.text)
const light = tokens(lightBlock.text, dark) // a light token left out falls back to the dark value, which is a bug the pairs below usually catch

// colour value -> [r, g, b, a] (0..255, 0..1)
function parse(v) {
  let m = /^#([0-9a-f]{6})$/i.exec(v)
  if (m) return [0, 2, 4].map((i) => parseInt(m[1].slice(i, i + 2), 16)).concat(1)
  m = /^rgba?\(\s*(\d+)\s+(\d+)\s+(\d+)(?:\s*\/\s*([\d.]+))?\s*\)$/.exec(v)
  if (m) return [+m[1], +m[2], +m[3], m[4] === undefined ? 1 : +m[4]]
  throw new Error(`cannot parse colour: ${v}`)
}
const over = (fg, bg) => [0, 1, 2].map((i) => fg[i] * fg[3] + bg[i] * (1 - fg[3])).concat(1)
const lum = ([r, g, b]) => {
  const f = (c) => ((c /= 255) <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4)
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)
}
const ratio = (a, b) => {
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p)
  return (x + 0.05) / (y + 0.05)
}

// [foreground, background, minimum, what it is]. 4.5 for text, 3 for UI boundaries and meaningful graphics.
const surfaces = ['--bg', '--card', '--raised']
const pairs = [
  ...surfaces.flatMap((s) => ['--text', '--text-2', '--text-muted'].map((f) => [f, s, 4.5, `${f} on ${s}`])),
  ['--text', '--raised-hover', 4.5, 'text on hover surface'],
  ['--text-2', '--raised-hover', 4.5, 'secondary text on hover surface'],
  ['--text-muted', '--raised-hover', 4.5, 'muted text on hover surface'],
  ['--text', '--surface-deep', 4.5, 'nav text'],
  ['--text-2', '--surface-deep', 4.5, 'nav and tab links'],
  ...['--bg', '--card', '--raised', '--raised-hover', '--surface-deep'].map((s) => ['--accent', s, 4.5, `accent text on ${s}`]),
  ['--accent-ink', '--accent', 4.5, 'primary button'],
  ['--accent-ink', '--accent-hover', 4.5, 'primary button hover'],
  ['--accent', '--chip-solid', 4.5, 'score tag in a row'],
  ['--accent', '--chip-bg', 4.5, 'score tag on a cover (tag colour alone, cover behind it ignored)'],
  ...surfaces.map((s) => ['--negative', s, 4.5, `error text on ${s}`]),
  ['--on-negative', '--negative', 4.5, 'filled danger button'],
  ['--on-negative', '--negative-hover', 4.5, 'filled danger button hover'],
  ['--negative', '--negative-soft-strong', 4.5, 'danger button pressed (over card)'],
  ['--text-muted', '--card', 4.5, 'disabled button text'],
  // UI boundaries and graphics, 3:1
  ...surfaces.map((s) => ['--control-border', s, 3, `input and switch edge on ${s}`]),
  ...['--bg', '--card', '--surface-deep'].map((s) => ['--accent', s, 3, `focus ring and selected edge on ${s}`]),
  ...['--positive', '--warning', '--negative'].map((f) => [f, '--card', 3, `status colour ${f}`]),
  ['--text-2', '--raised', 3, 'switch knob and chevron'],
  ['--text-muted', '--raised', 3, 'chart bar on its track'],
  ['--accent', '--raised', 3, 'leading chart bar on its track'],
]

let fail = 0
// a translucent background is laid over the page colour; the soft danger tint sits on a card
const solid = (t, name, under) => { const c = parse(t[name]); return c[3] < 1 ? over(c, under) : c }
for (const [label, t] of [['dark', dark], ['light', light]]) {
  for (const [f, b, need, what] of pairs) {
    const bg = solid(t, b, parse(b === '--negative-soft-strong' ? t['--card'] : t['--bg']))
    const r = ratio(solid(t, f, bg), bg)
    if (process.argv.includes('--list')) console.log(`${label.padEnd(5)} ${r.toFixed(2).padStart(6)} (need ${need})  ${what}`)
    if (r < need) { fail++; console.error(`FAIL ${label}: ${what}: ${r.toFixed(2)} < ${need}`) }
  }
  console.log(`${label}: ${pairs.length} pairs checked`)
}

// every token the light theme overrides must exist in dark, and the light block must not miss a colour
for (const name of Object.keys(dark)) {
  if (!/^--(surface|bg|card|raised|control-border|hairline|scrim|shadow|selection|chip|text|accent|positive|negative|warning|on-)/.test(name)) continue
  if (!(name in tokens(lightBlock.text))) { fail++; console.error(`FAIL light theme does not set ${name}`) }
}

// no colour literal outside the two token blocks
const outside = (css.slice(0, darkBlock.start) + css.slice(darkBlock.end, lightBlock.start) + css.slice(lightBlock.end))
for (const m of outside.matchAll(/#[0-9a-fA-F]{3,8}\b(?![\w-])|\brgba?\(|\bhsla?\(/g)) {
  const line = outside.slice(0, m.index).split('\n').length
  fail++
  console.error(`FAIL colour literal outside the token blocks: ${m[0]} (near line ${line} of the css without the token blocks)`)
}

// the pre-paint script repeats --bg for the browser bar
const init = readFileSync(new URL('public/theme-init.js', root), 'utf8')
for (const t of [dark, light]) {
  if (!init.toLowerCase().includes(t['--bg'].toLowerCase())) { fail++; console.error(`FAIL public/theme-init.js does not contain --bg ${t['--bg']}`) }
}

if (fail) { console.error(`${fail} problem(s)`); process.exit(1) }
console.log('contrast ok')
