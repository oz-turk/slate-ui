import { writable } from 'svelte/store'
export const mode   = writable('preview')
export const pinned = writable(true)
