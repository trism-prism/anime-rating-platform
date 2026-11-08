// Postbuild: copy dist/index.html to dist/<route>/index.html with that route's title and share tags, so
// crawlers (which do not run JS) see per-page previews. Vercel serves real files before the SPA rewrite.
// Titles/descriptions come from src/pageMeta.json, the same file the pages pass to useTitle.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const dist = fileURLToPath(new URL('../dist/', import.meta.url))
const pages = JSON.parse(readFileSync(new URL('../src/pageMeta.json', import.meta.url), 'utf8'))
const html = readFileSync(dist + 'index.html', 'utf8')
const origin = html.match(/property="og:url" content="(https?:\/\/[^/"]+)/)?.[1]
if (!origin) throw new Error('og:url missing in dist/index.html')
const esc = (s) => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;')

// replace one tag's value; throw if the tag is missing so a template edit cannot silently skip a route
const swap = (src, re, value) => {
  if (!re.test(src)) throw new Error('tag not found: ' + re)
  return src.replace(re, (_, a, b) => a + value + b)
}

for (const [path, { title, description }] of Object.entries(pages)) {
  const full = `${title} · AnimeRate`
  let out = html
  out = swap(out, /(<title>)[^<]*(<\/title>)/, esc(full))
  out = swap(out, /(<meta name="description" content=")[^"]*(")/, esc(description))
  out = swap(out, /(<meta property="og:title" content=")[^"]*(")/, esc(full))
  out = swap(out, /(<meta property="og:description" content=")[^"]*(")/, esc(description))
  out = swap(out, /(<meta property="og:url" content=")[^"]*(")/, origin + path)
  mkdirSync(dist + path.slice(1), { recursive: true })
  writeFileSync(dist + path.slice(1) + '/index.html', out)
}
console.log(`prerender-meta: ${Object.keys(pages).length} routes`)
