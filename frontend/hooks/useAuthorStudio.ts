'use client';
// File: hooks/useAuthorStudio.ts
import { useState, useEffect, useCallback } from 'react';
import { api } from '@/lib/api';
import { useAuth } from '@/context/AuthContext';
import type { Book, Chapter, AuthorStats } from '@/types';

export function useAuthorStudio() {
  const { user, isAuthenticated, isLoading: authLoading } = useAuth();
  const [books, setBooks] = useState<Book[]>([]);
  const [stats, setStats] = useState<AuthorStats | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const fetchAll = useCallback(async () => {
    if (!isAuthenticated || (user?.role !== 'Author' && user?.role !== 'Admin')) {
      setIsLoading(false);
      return;
    }
    setIsLoading(true);
    try {
      const [b, s] = await Promise.all([
        api.get<Book[]>('/writerstudio/books'),
        api.get<AuthorStats>('/writerstudio/stats'),
      ]);
      setBooks(b);
      setStats(s);
    } catch { /* not author */ }
    finally { setIsLoading(false); }
  }, [isAuthenticated, user]);

  useEffect(() => {
    if (!authLoading) {
      Promise.resolve().then(fetchAll);
    }
  }, [fetchAll, authLoading]);

  const createBook = useCallback(async (data: {
    title: string; description: string; coverImage: string; genreIds: number[]; status: string;
  }) => {
    const res = await api.post<{ bookId: number }>('/writerstudio/book', data);
    await fetchAll();
    return res.bookId;
  }, [fetchAll]);

  const updateBook = useCallback(async (bookId: number, data: {
    title: string; description: string; coverImage: string; genreIds: number[]; status: string;
  }) => {
    await api.put(`/writerstudio/book/${bookId}`, data);
    await fetchAll();
  }, [fetchAll]);

  const createChapter = useCallback(async (bookId: number, data: {
    title: string; content: string; sequenceNumber: number; price: number; isFree: boolean; status: string;
  }) => {
    await api.post(`/writerstudio/book/${bookId}/chapter`, data);
    await fetchAll();
  }, [fetchAll]);

  const getBookChapters = useCallback(async (bookId: number): Promise<Chapter[]> => {
    return api.get<Chapter[]>(`/writerstudio/book/${bookId}/chapters`);
  }, []);

  const updateChapter = useCallback(async (chapterId: number, data: Partial<Chapter>) => {
    await api.put(`/writerstudio/chapter/${chapterId}`, data);
  }, []);

  const deleteChapter = useCallback(async (chapterId: number) => {
    await api.delete(`/writerstudio/chapter/${chapterId}`);
  }, []);

  return { books, stats, isLoading, createBook, updateBook, createChapter, getBookChapters, updateChapter, deleteChapter, refetch: fetchAll };
}
