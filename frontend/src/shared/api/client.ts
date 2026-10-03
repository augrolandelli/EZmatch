import axios from 'axios'

/** Cliente HTTP único del panel. En dev usa el proxy de Vite; en prod, VITE_API_URL. */
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? '',
})
