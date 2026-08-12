// Crawler-friendly share tags for /series/:id and /titles/:id (rewritten here by vercel.json). Real users get the same
// SPA shell, only the head tags differ. Any API failure returns the plain shell, so the SPA shows its own not-found page.
import { describe, render, safeCover } from './_render.js'

const API = 'https://anime-rating-platform-production.up.railway.app'
const ORIGIN = 'https://anime-rating-platform-pi.vercel.app'
const timeout = () => AbortSignal.timeout(4000)

async function entity(kind, id) {
  const r = await fetch(`${API}/${kind}/${id}`, { signal: timeout() })
  if (!r.ok) return null
  const d = await r.json()
  return d?.name ? { name: String(d.name), description: describe(d.synopsis), cover: safeCover(d.coverUrl), path: `/${kind}/${id}` } : null
}

export default async function handler(req, res) {
  const { kind, id } = req.query
  const host = req.headers['x-forwarded-host'] || req.headers.host
  try {
    const r = await fetch(`https://${host}/index.html`, { signal: timeout() })
    if (!r.ok) throw new Error('shell ' + r.status)
    const shell = await r.text()
    let html = shell
    let found = false
    if ((kind === 'series' || kind === 'titles') && /^\d{1,9}$/.test(id)) {
      try {
        const meta = await entity(kind, id)
        if (meta) {
          html = render(shell, meta, ORIGIN)
          found = true
        }
      } catch {
        // API down or slow: serve the plain shell
      }
    }
    res.setHeader('Content-Type', 'text/html; charset=utf-8')
    res.setHeader('Cache-Control', found ? 's-maxage=3600, stale-while-revalidate=86400' : 's-maxage=60')
    res.status(200).send(html)
  } catch {
    res.setHeader('Cache-Control', 'no-store')
    res.status(502).send('Temporarily unavailable')
  }
}
