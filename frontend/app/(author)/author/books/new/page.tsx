'use client';
// File: app/(author)/author/books/new/page.tsx
import { useState } from 'react';
import { motion } from 'framer-motion';
import { useRouter } from 'next/navigation';
import { Plus, Trash2, ChevronLeft } from 'lucide-react';
import { useAuthorStudio } from '@/hooks/useAuthorStudio';
import { useGenres } from '@/hooks/useBooks';
import { useAuth } from '@/context/AuthContext';
import { Button } from '@/components/ui/Button';

interface ChapterDraft {
  title: string;
  content: string;
  sequenceNumber: number;
  price: number;
  isFree: boolean;
  status: 'Draft' | 'Published';
}

export default function NewBookPage() {
  const router = useRouter();
  const { isAuthenticated, user } = useAuth();
  const { createBook, createChapter } = useAuthorStudio();
  const { genres } = useGenres();

  // Book fields
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [coverImage, setCoverImage] = useState('');
  const [selectedGenres, setSelectedGenres] = useState<number[]>([]);

  // Chapters
  const [chapters, setChapters] = useState<ChapterDraft[]>([
    { title: '', content: '', sequenceNumber: 1, price: 10, isFree: false, status: 'Draft' },
  ]);

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [activeChapter, setActiveChapter] = useState(0);

  if (!isAuthenticated || (user?.role !== 'Author' && user?.role !== 'Admin')) {
    router.push('/home');
    return null;
  }

  function toggleGenre(id: number) {
    setSelectedGenres(prev => prev.includes(id) ? prev.filter(g => g !== id) : [...prev, id]);
  }

  function updateChapter(index: number, field: keyof ChapterDraft, value: unknown) {
    setChapters(prev => prev.map((ch, i) => i === index ? { ...ch, [field]: value } : ch));
  }

  function addChapter() {
    setChapters(prev => [...prev, {
      title: '', content: '', sequenceNumber: prev.length + 1, price: 10, isFree: false, status: 'Draft',
    }]);
    setActiveChapter(chapters.length);
  }

  function removeChapter(index: number) {
    if (chapters.length === 1) return;
    setChapters(prev => prev.filter((_, i) => i !== index).map((ch, i) => ({ ...ch, sequenceNumber: i + 1 })));
    setActiveChapter(Math.max(0, activeChapter - 1));
  }

  async function handleSubmit() {
    if (!title.trim()) { setError('عنوان کتاب الزامی است'); return; }
    setIsSubmitting(true);
    setError('');
    try {
      const nextBookStatus = chapters.some(ch => ch.status === 'Published') ? 'Published' : 'Draft';
      const bookId = await createBook({ title, description, coverImage, genreIds: selectedGenres, status: nextBookStatus });
      // Create chapters
      for (const ch of chapters) {
        if (ch.title.trim()) {
          await createChapter(bookId, ch);
        }
      }
      router.push('/author');
    } catch (e: unknown) {
      setError((e as Error).message || 'خطایی رخ داد');
    } finally {
      setIsSubmitting(false);
    }
  }

  const ch = chapters[activeChapter];

  return (
    <div style={{ maxWidth: 1100, margin: '0 auto', padding: '12px 20px 32px' }}>
      {/* Breadcrumb */}
      <button onClick={() => router.push('/author')} style={{ display: 'flex', alignItems: 'center', gap: 6, background: 'none', border: 'none', color: 'var(--accent-gold)', cursor: 'pointer', fontFamily: 'inherit', fontSize: '0.9rem', marginBottom: 24 }}>
        <ChevronLeft size={16} />بازگشت به استودیو
      </button>

      <h1 style={{ fontWeight: 900, fontSize: '1.5rem', marginBottom: 28 }}>
        <span className="text-gradient-gold">ایجاد کتاب جدید</span>
      </h1>

      <div className="new-book-grid">

        {/* Book Info */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border-default)', borderRadius: 'var(--radius-xl)', padding: '24px 20px' }}>
            <h2 style={{ fontWeight: 700, marginBottom: 18, fontSize: '1rem' }}>اطلاعات کتاب</h2>

            <label style={labelStyle}>عنوان کتاب *</label>
            <input value={title} onChange={e => setTitle(e.target.value)} placeholder="عنوان…" style={{ ...inputStyle, marginBottom: 14 }} />

            <label style={labelStyle}>توضیحات</label>
            <textarea value={description} onChange={e => setDescription(e.target.value)} placeholder="خلاصه کتاب را بنویسید…" rows={4} style={{ ...inputStyle, resize: 'vertical', marginBottom: 14 }} />

            <label style={labelStyle}>لینک تصویر جلد (URL)</label>
            <input value={coverImage} onChange={e => setCoverImage(e.target.value)} placeholder="https://…" style={{ ...inputStyle, marginBottom: 14 }} dir="ltr" />

            <label style={labelStyle}>ژانر</label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              {genres.map(g => (
                <button key={g.id} onClick={() => toggleGenre(g.id)} style={{
                  padding: '5px 12px', borderRadius: 99, cursor: 'pointer', fontFamily: 'inherit', fontSize: '0.82rem',
                  border: `1px solid ${selectedGenres.includes(g.id) ? 'var(--accent-gold)' : 'var(--border-default)'}`,
                  background: selectedGenres.includes(g.id) ? 'var(--accent-gold-glow)' : 'var(--bg-elevated)',
                  color: selectedGenres.includes(g.id) ? 'var(--accent-gold)' : 'var(--text-secondary)',
                }}>{g.name}</button>
              ))}
            </div>
          </div>
        </div>

        {/* Chapter Editor */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          {/* Chapter Tabs */}
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
            {chapters.map((ch, i) => (
              <button key={i} onClick={() => setActiveChapter(i)} style={{
                padding: '6px 14px', borderRadius: 'var(--radius-md)', cursor: 'pointer', fontFamily: 'inherit', fontSize: '0.82rem',
                border: `1px solid ${activeChapter === i ? 'var(--accent-gold)' : 'var(--border-default)'}`,
                background: activeChapter === i ? 'var(--accent-gold-glow)' : 'var(--bg-elevated)',
                color: activeChapter === i ? 'var(--accent-gold)' : 'var(--text-secondary)',
              }}>
                فصل {i + 1}
              </button>
            ))}
            <button onClick={addChapter} style={{ padding: '6px 10px', borderRadius: 'var(--radius-md)', border: '1px solid var(--border-default)', background: 'var(--bg-elevated)', color: 'var(--text-secondary)', cursor: 'pointer' }}>
              <Plus size={14} />
            </button>
          </div>

          {/* Active Chapter Form */}
          {ch && (
            <motion.div key={activeChapter} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }}
              style={{ background: 'var(--bg-card)', border: '1px solid var(--border-default)', borderRadius: 'var(--radius-xl)', padding: '24px 20px', flex: 1, display: 'flex', flexDirection: 'column', gap: 14 }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <h3 style={{ fontWeight: 700, fontSize: '1rem' }}>فصل {activeChapter + 1}</h3>
                {chapters.length > 1 && (
                  <button onClick={() => removeChapter(activeChapter)} style={{ background: 'rgba(244,63,94,0.1)', border: '1px solid var(--accent-rose)', borderRadius: 'var(--radius-sm)', padding: '4px 8px', cursor: 'pointer', color: 'var(--accent-rose)', display: 'flex' }}>
                    <Trash2 size={14} />
                  </button>
                )}
              </div>

              <div>
                <label style={labelStyle}>عنوان فصل</label>
                <input value={ch.title} onChange={e => updateChapter(activeChapter, 'title', e.target.value)} placeholder="عنوان فصل…" style={inputStyle} />
              </div>

              <div className="chapter-meta-row">
                <div style={{ flex: 1 }}>
                  <label style={labelStyle}>قیمت (سکه)</label>
                  <input type="number" value={ch.price} onChange={e => updateChapter(activeChapter, 'price', Number(e.target.value))} min={0} style={{ ...inputStyle, direction: 'ltr' }} disabled={ch.isFree} />
                </div>
                <div style={{ display: 'flex', alignItems: 'flex-end', paddingBottom: 1 }}>
                  <button onClick={() => updateChapter(activeChapter, 'isFree', !ch.isFree)} style={{
                    padding: '9px 16px', borderRadius: 'var(--radius-md)', cursor: 'pointer', fontFamily: 'inherit', fontSize: '0.85rem',
                    border: `1px solid ${ch.isFree ? 'var(--accent-emerald)' : 'var(--border-default)'}`,
                    background: ch.isFree ? 'rgba(16,185,129,0.15)' : 'var(--bg-elevated)',
                    color: ch.isFree ? 'var(--accent-emerald)' : 'var(--text-secondary)',
                  }}>
                    {ch.isFree ? '✓ رایگان' : 'رایگان؟'}
                  </button>
                </div>
                <div>
                  <label style={labelStyle}>وضعیت</label>
                  <select value={ch.status} onChange={e => updateChapter(activeChapter, 'status', e.target.value)} style={{ ...inputStyle, cursor: 'pointer' }}>
                    <option value="Draft">پیش‌نویس</option>
                    <option value="Published">منتشر</option>
                  </select>
                </div>
              </div>

              <div style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
                <label style={labelStyle}>محتوای فصل</label>
                <textarea
                  value={ch.content}
                  onChange={e => updateChapter(activeChapter, 'content', e.target.value)}
                  placeholder="متن فصل را اینجا بنویسید…"
                  style={{ ...inputStyle, flex: 1, minHeight: 320, resize: 'vertical', lineHeight: 1.9, fontSize: '0.95rem' }}
                />
              </div>
            </motion.div>
          )}
        </div>
      </div>

      {/* Submit */}
      {error && <p style={{ color: 'var(--accent-rose)', textAlign: 'center', marginTop: 16 }}>{error}</p>}
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 24, gap: 10 }}>
        <Button variant="ghost" onClick={() => router.push('/author')}>انصراف</Button>
        <Button isLoading={isSubmitting} onClick={handleSubmit} size="lg">
          ذخیره کتاب
        </Button>
      </div>
    </div>
  );
}

const labelStyle: React.CSSProperties = {
  display: 'block', fontSize: '0.82rem',
  color: 'var(--text-secondary)', marginBottom: 6,
};

const inputStyle: React.CSSProperties = {
  width: '100%', padding: '10px 14px',
  background: 'var(--bg-elevated)',
  border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-md)',
  color: 'var(--text-primary)',
  fontFamily: 'inherit', fontSize: '0.9rem', outline: 'none',
};
