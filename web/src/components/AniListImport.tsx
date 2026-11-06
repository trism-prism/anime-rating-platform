import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import type { ListImportStatus } from '../types'

// import a public AniList list; it runs on the server, so this polls until it is done
export default function AniListImport() {
  const [name, setName] = useState('')
  const [status, setStatus] = useState<ListImportStatus | null>(null)
  const [error, setError] = useState('')
  const [polling, setPolling] = useState(true)

  const load = useCallback(() => api<ListImportStatus>('/me/import/anilist').then((s) => { setStatus(s); setPolling(s.running) }).catch(() => setPolling(false)), [])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    if (!polling) return
    const t = setInterval(load, 3000)
    return () => clearInterval(t)
  }, [polling, load])

  async function start(e: React.FormEvent) {
    e.preventDefault()
    setError('')
    try {
      const s = await api<ListImportStatus>('/me/import/anilist', { method: 'POST', body: JSON.stringify({ userName: name }) })
      setStatus(s)
      setPolling(true)
    } catch (err) {
      setError((err as Error).message)
    }
  }

  const r = status?.result
  return (
    <>
      <h2>Import from AniList</h2>
      <p className="muted hint">
        Copies the titles on your public AniList list that exist here. Scores are not imported. Imported entries are visible to only you; change that on My list.
        Your list must be public. You can import once every 10 minutes.
      </p>
      <form className="row" onSubmit={start}>
        <input value={name} maxLength={20} placeholder="AniList username" aria-label="AniList username" onChange={(e) => setName(e.target.value)} />
        <button disabled={!name.trim() || status?.running}>Import</button>
      </form>
      {status?.running && <p className="muted">Importing</p>}
      {error && <p className="error">{error}</p>}
      {status?.error && <p className="error">{status.error}</p>}
      {r && <p>Added {r.added}, already on your list {r.alreadyOnList}, not found here {r.unmatched}.</p>}
    </>
  )
}
