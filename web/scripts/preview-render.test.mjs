// Run: node scripts/preview-render.test.mjs  (checks the share-tag swap against a hostile name and synopsis)
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, render, safeCover } from '../api/_render.js'

const shell = readFileSync(new URL('../index.html', import.meta.url), 'utf8')
const origin = 'https://anime-rating-platform-pi.vercel.app'
const evil = `"><script>alert(1)</script> $& $1 'x' & </title>`
const out = render(shell, { name: evil, description: describe(`<i>${evil}</i><br>` + 'word '.repeat(100)), cover: safeCover('https://s4.anilist.co/c.jpg?a=1&b="2"'), path: '/series/7' }, origin)

assert(!out.includes('<script>alert'), 'raw script tag leaked')
assert(!out.includes('"><script>'), 'attribute break-out')
assert.equal((out.match(/<title>/g) || []).length, 1)
assert(out.includes('&lt;script&gt;alert(1)&lt;/script&gt; $&amp; $1 &#39;x&#39; &amp; &lt;/title&gt; · GengaRate'), 'name not escaped literally')
assert(out.includes(`<meta property="og:url" content="${origin}/series/7" />`))
assert(out.includes('content="https://s4.anilist.co/c.jpg?a=1&amp;b=%222%22"'), 'cover not escaped')
assert(!out.includes('og:image:width') && out.includes('twitter:card" content="summary"'))
assert(out.includes('<script type="module" src="/src/main.tsx">'), 'app script tag lost')
assert(describe('word '.repeat(100)).length <= 201 && describe('word '.repeat(100)).endsWith('…'), 'description not trimmed')
assert.equal(describe('<i>a</i><br>b &amp; c'), 'a b & c')

// no cover: site image and its size tags stay; empty description keeps the shell's
const plain = render(shell, { name: 'Naruto', description: '', cover: null, path: '/titles/2' }, origin)
assert(plain.includes('og-image.png') && plain.includes('og:image:width') && plain.includes('summary_large_image'))
assert(plain.includes('Rate anime and manga on story'))
assert.equal(safeCover('javascript:alert(1)'), null)
assert.equal(safeCover('http://x.test/a.jpg'), null)
assert.equal(safeCover('/relative.jpg'), null)
console.log('preview-render: ok')
