const publicUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';
const API_BASE = typeof window === 'undefined' 
  ? (process.env.INTERNAL_API_URL || publicUrl) 
  : publicUrl;

function getToken(): string | null {
  if (typeof window === 'undefined') return null;
  return localStorage.getItem('ketabino_token');
}

async function request<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const token = getToken();

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string>),
  };

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  const res = await fetch(`${API_BASE}${endpoint}`, {
    ...options,
    headers,
  });

  if (!res.ok) {
    let message = 'خطایی رخ داد';
    let data: unknown = null;
    try {
      data = await res.json();
      if (data && typeof data === 'object') {
        const body = data as { message?: string; Message?: string; error?: string; Error?: string; title?: string };
        message = body.message || body.Message || body.error || body.Error || body.title || message;
      }
    } catch {
      message = res.statusText || message;
    }
    const err = new Error(message) as Error & { status: number; data?: unknown };
    err.status = res.status;
    err.data = data;
    throw err;
  }

  // Handle empty body (204)
  const text = await res.text();
  return text ? JSON.parse(text) : ({} as T);
}

// ─── Typed helpers ─────────────────────────────────────────────────────────────
export const api = {
  get: <T>(url: string) => request<T>(url, { method: 'GET' }),
  post: <T>(url: string, body?: unknown) =>
    request<T>(url, { method: 'POST', body: JSON.stringify(body) }),
  put: <T>(url: string, body?: unknown) =>
    request<T>(url, { method: 'PUT', body: JSON.stringify(body) }),
  delete: <T>(url: string) => request<T>(url, { method: 'DELETE' }),
};
