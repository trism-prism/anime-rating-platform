import { useEffect } from 'react'

// sets document.title to "<page> · GengaRate"; pass nothing (or null while loading) for the bare name
export function useTitle(page?: string | null) {
  useEffect(() => {
    document.title = page ? `${page} · GengaRate` : 'GengaRate'
  }, [page])
}
