import { useCallback, useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'

type AdminUser = { id: string; userName: string; email: string; emailVerified: boolean; isAdmin: boolean }
type Page = { total: number; page: number; pageSize: number; items: AdminUser[] }

const PAGE_SIZE = 20

export default function Admin() {
  const { isAdmin, loading } = useAuth()
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('') // applied search
  const [page, setPage] = useState(1)
  const [data, setData] = useState<Page | null>(null)
  const [error, setError] = useState('')
  const [importing, setImporting] = useState(false)
  const [importResult, setImportResult] = useState<Record<string, unknown> | null>(null)
  const [importError, setImportError] = useState('')

  const load = useCallback(() => {
    api<Page>(`/admin/users?search=${encodeURIComponent(query)}&page=${page}&pageSize=${PAGE_SIZE}`)
      .then(setData)
      .catch((err) => setError(err.message))
  }, [query, page])

  useEffect(() => { if (isAdmin) load() }, [isAdmin, load])

  if (loading) return <p className="muted">Loading...</p>
  if (!isAdmin) return <Navigate to="/" replace />

  async function toggle(u: AdminUser) {
    setError('')
    try {
      await api(`/admin/users/${u.id}/admin`, { method: u.isAdmin ? 'DELETE' : 'POST' })
      load()
    } catch (err) {
      setError((err as Error).message)
    }
  }

  async function runImport() {
    setImporting(true)
    setImportResult(null)
    setImportError('')
    try {
      setImportResult(await api<Record<string, unknown>>('/admin/import', { method: 'POST' }))
    } catch (err) {
      setImportError((err as Error).message)
    } finally {
      setImporting(false)
    }
  }

  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <>
      <h1>Admin</h1>

      <h2>Import titles</h2>
      <div className="row">
        <button onClick={runImport} disabled={importing}>{importing ? 'Importing...' : 'Run import'}</button>
        <span className="muted">This can take a few minutes.</span>
      </div>
      {importResult && (
        <p className="muted">Done: {Object.entries(importResult).map(([k, v]) => `${k} ${String(v)}`).join(', ')}</p>
      )}
      {importError && <p className="error">{importError}</p>}

      <h2>Users</h2>
      <form className="filters" onSubmit={(e) => { e.preventDefault(); setPage(1); setQuery(search) }}>
        <input placeholder="Search display name or email" value={search} onChange={(e) => setSearch(e.target.value)} />
        <button type="submit">Search</button>
      </form>
      {error && <p className="error">{error}</p>}
      <table className="table">
        <thead>
          <tr><th>Display name</th><th>Email</th><th>Verified</th><th>Admin</th><th /></tr>
        </thead>
        <tbody>
          {data?.items.map((u) => (
            <tr key={u.id}>
              <td>{u.userName}</td>
              <td>{u.email}</td>
              <td>{u.emailVerified ? 'Yes' : 'No'}</td>
              <td>{u.isAdmin ? 'Yes' : 'No'}</td>
              <td><button onClick={() => toggle(u)}>{u.isAdmin ? 'Remove admin' : 'Make admin'}</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="pager">
        <button disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
        <span className="muted">Page {page} of {pages}</span>
        <button disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
      </div>
    </>
  )
}
