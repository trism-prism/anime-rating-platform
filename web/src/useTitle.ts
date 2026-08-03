import { useEffect } from 'react'

// sets document.title to "<page> · AnimeRate"; pass nothing (or null while loading) for the bare name
export function useTitle(page?: string | null) {
  useEffect(() => {
    document.title = page ? `${page} · AnimeRate` : 'AnimeRate'
  }, [page])
}
