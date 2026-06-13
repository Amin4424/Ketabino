'use client';
// File: app/(reader)/books/[id]/chapters/[chapterId]/page.tsx
import { use, useState, useEffect, useMemo, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useRouter } from 'next/navigation';
import { Link } from 'next-view-transitions';
import { Lock, Wallet, ArrowRight, ArrowLeft, Minus, Plus, Highlighter, ChevronLeft } from 'lucide-react';
import { useChapter } from '@/hooks/useChapter';
import { useBookChapters } from '@/hooks/useBook';
import { useHighlights, useReadingProgress } from '@/hooks/useReadingProgress';
import { useWallet } from '@/hooks/useWallet';
import { useAuth } from '@/context/AuthContext';
import { Modal } from '@/components/ui/Modal';
import { Button } from '@/components/ui/Button';
import { Skeleton } from '@/components/ui/Skeleton';
import { formatCoins } from '@/utils/format';
import type { Highlight } from '@/types';

export default function ChapterPage({ params }: { params: Promise<{ id: string; chapterId: string }> }) {
  const { id, chapterId } = use(params);
  const bookId = Number(id);
  const chId = Number(chapterId);
  const router = useRouter();
  const { isAuthenticated } = useAuth();

  const { chapter, isLoading, isLocked, purchase } = useChapter(chId);
  const { chapters } = useBookChapters(bookId);
  const { wallet, refetch: refetchWallet } = useWallet();
  const { highlights, addHighlight, fetchHighlights } = useHighlights(chId);
  const { updateProgress } = useReadingProgress(bookId);

  const [fontSize, setFontSize] = useState(18);
  const [textColor, setTextColor] = useState('var(--text-primary)');
  const [purchaseModal, setPurchaseModal] = useState(false);
  const [isPurchasing, setIsPurchasing] = useState(false);
  const [purchaseError, setPurchaseError] = useState('');
  const [highlightColor, setHighlightColor] = useState('#C9A84C');
  const [selectedText, setSelectedText] = useState('');
  const [highlightPopover, setHighlightPopover] = useState<{ x: number; y: number } | null>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const readerSyncRef = useRef({ chId, fetchHighlights, updateProgress });

  useEffect(() => {
    readerSyncRef.current = { chId, fetchHighlights, updateProgress };
  });

  useEffect(() => {
    if (!isLocked) return;
    Promise.resolve().then(() => setPurchaseModal(true));
  }, [isLocked]);

  useEffect(() => {
    if (!chapter || !isAuthenticated) return;
    Promise.resolve().then(() => {
      const { chId: currentChapterId, fetchHighlights: fetchCurrentHighlights, updateProgress: updateCurrentProgress } = readerSyncRef.current;
      fetchCurrentHighlights();
      updateCurrentProgress(currentChapterId, 0);
    });
  }, [chapter, isAuthenticated]);

  const currentIdx = chapters.findIndex(c => c.id === chId);
  const prevChapter = chapters[currentIdx - 1];
  const nextChapter = chapters[currentIdx + 1];
  const highlightedSegments = useMemo(
    () => buildHighlightedSegments(chapter?.content ?? '', highlights),
    [chapter?.content, highlights]
  );

  function handleTextSelection() {
    const sel = window.getSelection();
    if (!sel || sel.isCollapsed) { setHighlightPopover(null); return; }
    const text = sel.toString().trim();
    if (!text) { setHighlightPopover(null); return; }
    const range = sel.getRangeAt(0);
    const rect = range.getBoundingClientRect();
    setSelectedText(text);
    // Use fixed positioning relative to viewport
    setHighlightPopover({ x: rect.left + rect.width / 2, y: rect.top - 52 });
  }

  async function handleAddHighlight() {
    if (!selectedText || !isAuthenticated) return;
    const offsets = getCurrentSelectionOffsets(contentRef.current);
    if (!offsets) return;
    await addHighlight(
      offsets.start, offsets.end,
      selectedText, highlightColor
    );
    setHighlightPopover(null);
    setSelectedText('');
    window.getSelection()?.removeAllRanges();
  }

  async function handlePurchase() {
    if (!isAuthenticated) { router.push('/login'); return; }
    setIsPurchasing(true);
    setPurchaseError('');
    try {
      await purchase();
      setPurchaseModal(false);
      await refetchWallet();
    } catch (e: unknown) {
      setPurchaseError((e as Error).message || 'خطایی رخ داد');
    } finally {
      setIsPurchasing(false);
    }
  }

  const balanceOk = wallet && chapter && wallet.balance >= chapter.price;

  return (
    <div style={{ maxWidth: 760, margin: '0 auto', padding: 'var(--fixed-header-content-offset) 20px 24px' }}>

      {/* Breadcrumb */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 24, color: 'var(--text-muted)', fontSize: '0.85rem' }}>
        <Link href={`/books/${bookId}`} style={{ color: 'var(--accent-gold)', textDecoration: 'none', display: 'flex', alignItems: 'center', gap: 4 }}>
          <ChevronLeft size={14} />بازگشت به کتاب
        </Link>
        {chapter && <><span>/</span><span>{chapter.title}</span></>}
      </div>

      <div className="reading-toolbar">
        <span style={{ color: 'var(--text-muted)', fontSize: '0.82rem' }}>اندازه متن</span>
        <button onClick={() => setFontSize(f => Math.max(14, f - 1))} style={iconBtn}><Minus size={14} /></button>
        <span style={{ fontSize: '0.85rem', minWidth: 28, textAlign: 'center' }}>{fontSize}</span>
        <button onClick={() => setFontSize(f => Math.min(28, f + 1))} style={iconBtn}><Plus size={14} /></button>

        <div style={{ marginRight: 'auto', display: 'flex', gap: 8, alignItems: 'center' }}>
          {/* Text color picker */}
          <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem' }}>رنگ متن:</span>
          {(['var(--text-primary)', '#C9A84C', '#8B5CF6', '#10B981', '#F43F5E'] as const).map(c => (
            <button key={c} onClick={() => setTextColor(c)}
              style={{
                width: 20, height: 20, borderRadius: '50%',
                background: c === 'var(--text-primary)' ? 'var(--text-primary)' : c,
                border: `2.5px solid ${textColor === c ? 'white' : 'transparent'}`,
                cursor: 'pointer',
                boxShadow: textColor === c ? '0 0 0 1px rgba(255,255,255,0.4)' : 'none',
              }} />
          ))}

          {/* Highlight color picker */}
          <span style={{ color: 'var(--text-muted)', fontSize: '0.75rem', marginRight: 4 }}>هایلایت:</span>
          {(['#C9A84C', '#8B5CF6', '#10B981', '#F43F5E'] as const).map(c => (
            <button key={c} onClick={() => setHighlightColor(c)}
              style={{ width: 20, height: 20, borderRadius: '50%', background: c, border: `2px solid ${highlightColor === c ? 'white' : 'transparent'}`, cursor: 'pointer' }} />
          ))}

        </div>
      </div>

      {/* Chapter Content */}
      {isLoading ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className={`h-4 ${i % 3 === 2 ? 'w-3/4' : 'w-full'}`} />)}
        </div>
      ) : isLocked ? (
        <div style={{ textAlign: 'center', padding: '80px 20px', background: 'var(--bg-card)', borderRadius: 'var(--radius-xl)', border: '1px solid var(--border-default)' }}>
          <div style={{ fontSize: 52, marginBottom: 16 }}>🔒</div>
          <h2 style={{ fontWeight: 700, marginBottom: 8 }}>این فصل قفل است</h2>
          <p style={{ color: 'var(--text-secondary)', marginBottom: 24 }}>
            برای مطالعه این فصل نیاز به خرید دارید.
          </p>
          <Button onClick={() => setPurchaseModal(true)}>
            <Wallet size={15} />خرید این فصل
          </Button>
        </div>
      ) : chapter?.content ? (
        <div
          ref={contentRef}
          onMouseUp={handleTextSelection}
          style={{
            fontSize: fontSize, lineHeight: 2.2,
            color: textColor,
            fontFamily: 'Vazirmatn, serif',
            userSelect: 'text',
            whiteSpace: 'pre-wrap',
          }}
        >
          {highlightedSegments.map((segment, index) => segment.highlight ? (
            <mark
              key={`${segment.start}-${segment.end}-${index}`}
              style={{
                background: withAlpha(segment.highlight.color, '55'),
                color: 'inherit',
                borderRadius: 4,
                padding: '0 2px',
              }}
            >
              {segment.text}
            </mark>
          ) : (
            <span key={`${segment.start}-${segment.end}-${index}`}>{segment.text}</span>
          ))}
        </div>
      ) : (
        <div style={{ textAlign: 'center', padding: '80px 20px', color: 'var(--text-muted)' }}>
          <p>فعلا محتوای موجود نیست</p>
        </div>
      )}

      {/* Highlight popover - fixed so it stays near selection regardless of scroll */}
      <AnimatePresence>
        {highlightPopover && selectedText && (
          <motion.div
            initial={{ opacity: 0, scale: 0.9 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.9 }}
            style={{
              position: 'fixed', top: highlightPopover.y, left: highlightPopover.x,
              transform: 'translateX(-50%)',
              background: 'var(--bg-elevated)',
              border: '1px solid var(--border-default)',
              borderRadius: 'var(--radius-md)',
              padding: '6px 10px',
              display: 'flex', gap: 8, alignItems: 'center',
              zIndex: 1000, boxShadow: 'var(--shadow-card)',
            }}
          >
            <button onClick={handleAddHighlight} style={{ display: 'flex', alignItems: 'center', gap: 6, background: 'none', border: 'none', color: highlightColor, cursor: 'pointer', fontFamily: 'inherit', fontSize: '0.82rem' }}>
              <Highlighter size={14} />هایلایت
            </button>
            <button onClick={() => { setHighlightPopover(null); window.getSelection()?.removeAllRanges(); }}
              style={{ background: 'none', border: 'none', color: 'var(--text-muted)', cursor: 'pointer', fontSize: '0.75rem' }}>
              ✕
            </button>
          </motion.div>
        )}
      </AnimatePresence>

      <div className="chapter-nav-row">
        {prevChapter && (
          <Link href={`/books/${bookId}/chapters/${prevChapter.id}`} style={{ flex: 1, textDecoration: 'none' }}>
            <Button variant="outline" size="md" style={{ width: '100%', justifyContent: 'flex-start', gap: 8 }}>
              <ArrowRight size={15} />فصل قبلی: {prevChapter.title}
            </Button>
          </Link>
        )}
        {nextChapter && (
          <Link href={`/books/${bookId}/chapters/${nextChapter.id}`} style={{ flex: 1, textDecoration: 'none' }}>
            <Button variant={nextChapter.isPurchased || nextChapter.isFree ? 'gold' : 'outline'} size="md" style={{ width: '100%', justifyContent: 'flex-end', gap: 8 }}>
              فصل بعدی: {nextChapter.title}<ArrowLeft size={15} />
            </Button>
          </Link>
        )}
      </div>

      {/* Purchase Modal */}
      <Modal isOpen={purchaseModal} onClose={() => { if (!isLocked) setPurchaseModal(false); }} title="خرید فصل">
        {chapter && (
          <div style={{ textAlign: 'center' }}>
            <div style={{ fontSize: 44, marginBottom: 12 }}>📖</div>
            <h3 style={{ fontWeight: 700, marginBottom: 6 }}>{chapter.title}</h3>
            <div style={{
              display: 'inline-flex', alignItems: 'center', gap: 8,
              background: 'var(--accent-gold-glow)', border: '1px solid var(--accent-gold-dim)',
              borderRadius: 'var(--radius-md)', padding: '8px 20px', margin: '14px auto',
            }}>
              <span style={{ color: 'var(--accent-gold)', fontSize: '1.4rem', fontWeight: 900 }}>
                {formatCoins(chapter.price)}
              </span>
            </div>

            {wallet && (
              <p style={{ color: 'var(--text-secondary)', fontSize: '0.88rem', marginBottom: 6 }}>
                موجودی کیف پول شما: <span style={{ color: balanceOk ? 'var(--accent-emerald)' : 'var(--accent-rose)', fontWeight: 700 }}>{formatCoins(wallet.balance)}</span>
              </p>
            )}

            {purchaseError && (
              <p style={{ color: 'var(--accent-rose)', fontSize: '0.85rem', margin: '10px 0' }}>{purchaseError}</p>
            )}

            <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginTop: 20 }}>
              {balanceOk ? (
                <Button isLoading={isPurchasing} onClick={handlePurchase} size="lg">
                  <Lock size={15} />تأیید خرید — {formatCoins(chapter.price)}
                </Button>
              ) : (
                <>
                  <p style={{ color: 'var(--accent-rose)', fontSize: '0.85rem' }}>موجودی کافی نیست</p>
                  <Button onClick={() => router.push('/profile')} variant="outline">
                    <Wallet size={14} />شارژ کیف پول
                  </Button>
                </>
              )}
              {!isLocked && <Button variant="ghost" onClick={() => setPurchaseModal(false)}>انصراف</Button>}
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}

const iconBtn: React.CSSProperties = {
  background: 'var(--bg-elevated)', border: '1px solid var(--border-default)',
  borderRadius: 'var(--radius-sm)', padding: '5px', display: 'flex',
  alignItems: 'center', justifyContent: 'center',
  cursor: 'pointer', color: 'var(--text-secondary)',
};

interface HighlightSegment {
  text: string;
  start: number;
  end: number;
  highlight?: Highlight;
}

function buildHighlightedSegments(content: string, highlights: Highlight[]): HighlightSegment[] {
  if (!content) return [];

  const validHighlights = highlights
    .filter(item => item.startChar >= 0 && item.endChar > item.startChar && item.startChar < content.length)
    .map(item => ({ ...item, endChar: Math.min(item.endChar, content.length) }))
    .sort((a, b) => a.startChar - b.startChar || b.endChar - a.endChar);

  const segments: HighlightSegment[] = [];
  let cursor = 0;

  for (const highlight of validHighlights) {
    if (highlight.startChar < cursor) continue;

    if (highlight.startChar > cursor) {
      segments.push({
        text: content.slice(cursor, highlight.startChar),
        start: cursor,
        end: highlight.startChar,
      });
    }

    segments.push({
      text: content.slice(highlight.startChar, highlight.endChar),
      start: highlight.startChar,
      end: highlight.endChar,
      highlight,
    });
    cursor = highlight.endChar;
  }

  if (cursor < content.length) {
    segments.push({
      text: content.slice(cursor),
      start: cursor,
      end: content.length,
    });
  }

  return segments;
}

function getCurrentSelectionOffsets(container: HTMLElement | null): { start: number; end: number } | null {
  const selection = window.getSelection();
  if (!selection || selection.isCollapsed || !container || selection.rangeCount === 0) return null;

  const range = selection.getRangeAt(0);
  if (!container.contains(range.startContainer) || !container.contains(range.endContainer)) return null;

  const start = getTextOffset(container, range.startContainer, range.startOffset);
  const end = getTextOffset(container, range.endContainer, range.endOffset);
  if (start === null || end === null || start === end) return null;

  return {
    start: Math.min(start, end),
    end: Math.max(start, end),
  };
}

function getTextOffset(container: HTMLElement, targetNode: Node, targetOffset: number): number | null {
  let offset = 0;
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  let node = walker.nextNode();

  while (node) {
    if (node === targetNode) {
      return offset + targetOffset;
    }

    offset += node.textContent?.length ?? 0;
    node = walker.nextNode();
  }

  return null;
}

function withAlpha(color: string, alpha: string): string {
  return /^#[0-9a-fA-F]{6}$/.test(color) ? `${color}${alpha}` : color;
}
