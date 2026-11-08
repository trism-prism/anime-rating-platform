// Pure helpers for api/preview.js: swap one entity's title and share tags into the built index.html shell.
const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/'/g, '&#39;')

// AniList synopses carry <br>/<i> markup and entities; crawlers want plain text of a sane length
export function describe(raw, max = 200) {
  const text = String(raw ?? '')
    .replace(/<[^>]*>/g, ' ')
    .replace(/&(amp|lt|gt|quot|#39|nbsp);/g, (_, e) => ({ amp: '&', lt: '<', gt: '>', quot: '"', '#39': "'", nbsp: ' ' })[e])
    .replace(/\s+/g, ' ')
    .trim()
  if (text.length <= max) return text
  return text.slice(0, max).replace(/\s+\S*$/, '') + '…'
}

// swap one tag's value; a missing tag is left alone. The callback form keeps "$&" and friends in a hostile
// name from being read as replacement patterns.
const swap = (src, re, value) => src.replace(re, (_, a, b) => a + value + b)

// meta: { name, description ('' keeps the shell's), cover (absolute https url or null), path ('/series/1') }.
export function render(html, { name, description, cover, path }, origin) {
  const full = `${name} · GengaRate`
  let out = html
  out = swap(out, /(<title>)[^<]*(<\/title>)/, esc(full))
  out = swap(out, /(<meta property="og:title" content=")[^"]*(")/, esc(full))
  out = swap(out, /(<meta property="og:url" content=")[^"]*(")/, esc(origin + path))
  if (description) {
    out = swap(out, /(<meta name="description" content=")[^"]*(")/, esc(description))
    out = swap(out, /(<meta property="og:description" content=")[^"]*(")/, esc(description))
  }
  if (cover) {
    out = swap(out, /(<meta property="og:image" content=")[^"]*(")/, esc(cover))
    // the shell's size and alt describe the 1200x630 site image, not a portrait cover
    out = out.replace(/\s*<meta property="og:image:(?:width|height|alt)" content="[^"]*" ?\/?>/g, '')
    out = swap(out, /(<meta name="twitter:card" content=")[^"]*(")/, 'summary')
  }
  return out
}

// the API sends absolute https cover urls; anything else (relative, javascript:, http) is ignored
export function safeCover(url) {
  try {
    const u = new URL(url)
    return u.protocol === 'https:' ? u.href : null
  } catch {
    return null
  }
}
