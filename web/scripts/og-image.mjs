// Regenerates public/og-image.png (1200x630) from the HTML below, using the dark theme tokens in src/index.css.
// Playwright is not a dependency of this repo: run with it resolvable (see rate-prefill-race.mjs). Output is committed.
import { chromium } from 'playwright'
import { writeFileSync } from 'node:fs'
import { fileURLToPath, pathToFileURL } from 'node:url'

const font = (name, file) =>
  `@font-face{font-family:'${name}';src:url('${pathToFileURL(fileURLToPath(new URL(`../node_modules/@fontsource-variable/${file}-latin-wght-normal.woff2`, import.meta.url)))}');font-weight:100 900}`

const html = `<!doctype html><meta charset="utf-8"><style>
${font('Geist', 'geist/files/geist')}
${font('Space Grotesk', 'space-grotesk/files/space-grotesk')}
*{box-sizing:border-box;margin:0}
body{width:1200px;height:630px;background:#141311;color:#ece8e1;font-family:Geist,sans-serif;position:relative;overflow:hidden;padding:72px 80px;display:flex;flex-direction:column;justify-content:space-between}
body::before{content:'';position:absolute;right:-160px;top:-200px;width:620px;height:620px;border-radius:50%;background:radial-gradient(#ff7a5933,transparent 70%)}
.brand{display:flex;align-items:center;gap:14px;font:600 40px 'Space Grotesk';color:#ff7a59;letter-spacing:-.01em;position:relative}
h1{font:600 84px/1.04 Geist;letter-spacing:-.025em;max-width:820px;position:relative}
.sub{font-size:32px;line-height:1.35;color:#aca69c;max-width:760px;margin-top:24px;position:relative}
.row{display:flex;gap:16px;position:relative}
.chip{background:#262420;border:1px solid #3a3731;border-radius:14px;padding:16px 24px;display:flex;gap:14px;align-items:baseline;font-size:26px;color:#aca69c}
.chip b{font:600 40px 'Space Grotesk';color:#ff7a59}
</style>
<div class="brand"><svg width="44" height="44" viewBox="0 0 32 32" fill="none" stroke="#ff7a59" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><rect x="3" y="12" width="14" height="14" rx="3" opacity=".4"/><rect x="7" y="8" width="14" height="14" rx="3" fill="#141311" opacity=".7"/><rect x="11" y="4" width="14" height="14" rx="3" fill="#141311"/><path d="M14.5 13.5c2-5.5 4.5-5.5 7-1"/></svg>GengaRate</div>
<div><h1>Every key frame, rated</h1><p class="sub">Score story, art, characters and enjoyment. Keep lists and see what friends watched.</p></div>
<div class="row"><div class="chip"><b>9</b>Story</div><div class="chip"><b>8</b>Art</div><div class="chip"><b>9</b>Characters</div><div class="chip"><b>10</b>Enjoyment</div></div>`

const browser = await chromium.launch({ channel: 'chrome', headless: true })
const page = await browser.newPage({ viewport: { width: 1200, height: 630 } })
// about:blank cannot load file:// fonts, so render from a file
const tmp = new URL('../node_modules/.og.html', import.meta.url)
writeFileSync(tmp, html)
await page.goto(tmp.href)
await page.evaluate(() => document.fonts.ready)
await page.screenshot({ path: fileURLToPath(new URL('../public/og-image.png', import.meta.url)) })
await browser.close()
