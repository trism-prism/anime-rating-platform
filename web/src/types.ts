export type TitleType = 'Anime' | 'Manga'

export interface TitleSummary {
  id: number
  name: string
  coverUrl: string | null
  type: TitleType
  genres: string[]
  popularity: number
  averageOverall: number | null
  ratingCount: number
}

export interface TitleList {
  total: number
  items: TitleSummary[]
}

export interface VoiceActor {
  id: number
  name: string
  imageUrl: string | null
}

export interface TitleCharacter {
  characterId: number
  name: string
  imageUrl: string | null
  role: string | null
  voiceActor: VoiceActor | null
}

export interface Scores {
  story: number
  art: number
  characters: number
  enjoyment: number
  overall: number
}

export interface Review {
  userName: string
  overall: number
  story: number
  art: number
  characters: number
  enjoyment: number
  reviewText: string
  createdAt: string
}

export interface TitleDetail {
  id: number
  aniListId: number
  type: TitleType
  name: string
  coverUrl: string | null
  synopsis: string | null
  episodes: number | null
  chapters: number | null
  genres: string[]
  popularity: number
  characters: TitleCharacter[]
  averages: Scores | null
  ratingCount: number
  reviews: Review[]
}
