// One small module that talks to the backend. Every component imports from here.
const KEY = 'tx_session';
const BASE = import.meta.env.VITE_API_URL || ''; // empty in dev (Vite proxy handles /api)

export const session = {
  load: () => { try { return JSON.parse(localStorage.getItem(KEY)); } catch { return null; } },
  save: (s) => localStorage.setItem(KEY, JSON.stringify(s)),
  clear: () => localStorage.removeItem(KEY),
};

async function request(path, { method = 'GET', body } = {}) {
  const token = session.load()?.token;
  const res = await fetch(BASE + path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });

  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    try {
      const p = await res.json();
      message = p.title || p.detail || (p.errors ? Object.values(p.errors).flat().join(' ') : message);
    } catch { /* response had no JSON body */ }
    if (res.status === 401 && token) { session.clear(); window.location.reload(); }
    throw new Error(message);
  }
  return res.status === 204 ? null : res.json();
}

export const api = {
  login: (email, password) => request('/api/auth/login', { method: 'POST', body: { email, password } }),
  register: (email, fullName, password) =>
    request('/api/auth/register', { method: 'POST', body: { email, fullName, password } }),

  trips: () => request('/api/trips'),
  createTrip: (t) => request('/api/trips', { method: 'POST', body: t }),
  searchFlights: ({ origin, destination, date }) =>
    request(`/api/flights/search?origin=${origin}&destination=${destination}&date=${date}`),
  bookFlight: (tripId, body) => request(`/api/trips/${tripId}/bookings`, { method: 'POST', body }),

  reports: () => request('/api/expenses'),
  report: (id) => request(`/api/expenses/${id}`),
  createReport: (tripId, title) => request('/api/expenses', { method: 'POST', body: { tripId, title } }),
  addItem: (id, item) => request(`/api/expenses/${id}/items`, { method: 'POST', body: item }),
  submit: (id) => request(`/api/expenses/${id}/submit`, { method: 'POST' }),

  pending: () => request('/api/approvals'),
  approve: (id, comment) => request(`/api/approvals/${id}/approve`, { method: 'POST', body: { comment } }),
  reject: (id, comment) => request(`/api/approvals/${id}/reject`, { method: 'POST', body: { comment } }),
};

export const money = (n, c = 'CAD') =>
  new Intl.NumberFormat('en-CA', { style: 'currency', currency: c }).format(n);
export const inDays = (n) => new Date(Date.now() + n * 86400000).toISOString().slice(0, 10);
