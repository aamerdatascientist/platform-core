import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon } from '../icons';
import { Button, Sheet } from '../ui';

const ACCEPT = 'image/jpeg,image/png,image/webp,image/heic,application/pdf';

/**
 * Photos and files for an Attachment field (screen 06): a 3-column grid of 100px tiles. A
 * chosen file shows its thumbnail (or a PDF tile) with a size badge; tapping it opens a preview
 * with Remove. The last tile is the dashed "Take a photo" button, which opens the phone's camera
 * (and still lets the person pick a file or PDF). The hint names the accepted types and limit.
 */
export function AttachmentTiles({
  files,
  onChange,
  invalid,
  describedBy,
  labelledBy,
}: {
  files: File[];
  onChange: (files: File[]) => void;
  invalid?: boolean;
  describedBy?: string;
  labelledBy?: string;
}) {
  const { t, i18n } = useTranslation();
  const input = useRef<HTMLInputElement>(null);
  const [preview, setPreview] = useState<number | null>(null);
  const urls = useMemo(() => files.map((f) => (f.type.startsWith('image/') ? URL.createObjectURL(f) : null)), [files]);
  useEffect(() => () => urls.forEach((u) => u && URL.revokeObjectURL(u)), [urls]);
  const locale = i18n.language.startsWith('ar') ? 'ar-u-nu-latn' : i18n.language;
  const size = (bytes: number) =>
    new Intl.NumberFormat(locale, { style: 'unit', unit: 'megabyte', unitDisplay: 'short', maximumFractionDigits: 1 }).format(Math.max(0.1, bytes / 1024 / 1024));

  return (
    <>
      <div role="group" aria-labelledby={labelledBy} aria-describedby={describedBy} aria-invalid={invalid || undefined} className="grid grid-cols-3 gap-2">
        {files.map((file, i) => (
          <button
            key={`${file.name}-${i}`}
            type="button"
            onClick={() => setPreview(i)}
            aria-label={file.name}
            className="relative flex h-[100px] items-end overflow-hidden rounded-ui-xl bg-off p-1.5"
            style={urls[i] ? { backgroundImage: `url(${urls[i]})`, backgroundSize: 'cover', backgroundPosition: 'center' } : undefined}
          >
            {!urls[i] && (
              <span className="absolute inset-0 flex items-center justify-center text-[14px] font-bold text-ink-soft">PDF</span>
            )}
            <span dir="ltr" className="relative rounded-ui-xs px-1.5 py-0.5 text-[11px] font-bold text-white" style={{ background: 'rgba(19, 23, 28, 0.6)' }}>
              {size(file.size)}
            </span>
          </button>
        ))}
        <button
          type="button"
          onClick={() => input.current?.click()}
          className={`flex h-[100px] flex-col items-center justify-center gap-1.5 rounded-ui-xl border-[1.5px] border-dashed bg-panel text-[14px] font-bold text-accent ${
            invalid ? 'border-danger bg-danger-soft' : 'border-accent'
          }`}
        >
          <Icon name="camera" size={24} />
          {t('steps.takePhoto')}
        </button>
      </div>
      <input
        ref={input}
        type="file"
        accept={ACCEPT}
        capture="environment"
        className="hidden"
        aria-hidden="true"
        tabIndex={-1}
        onChange={(e) => {
          const picked = e.target.files?.[0];
          if (picked) onChange([...files, picked]);
          e.target.value = '';
        }}
      />
      <Sheet open={preview !== null} onClose={() => setPreview(null)} title={preview !== null ? files[preview]?.name ?? '' : ''}>
        {preview !== null && files[preview] && (
          <div className="flex flex-col gap-4">
            {urls[preview] ? (
              <img src={urls[preview]!} alt={files[preview].name} className="max-h-[50vh] w-full rounded-ui-xl object-contain" />
            ) : (
              <p className="rounded-ui-xl bg-field-muted p-6 text-center text-[15px] text-ink-soft" dir="ltr">
                {files[preview].name}
              </p>
            )}
            <Button
              variant="danger"
              block
              icon={<Icon name="trash" size={20} />}
              onClick={() => {
                onChange(files.filter((_, i) => i !== preview));
                setPreview(null);
              }}
            >
              {t('steps.removeFile')}
            </Button>
          </div>
        )}
      </Sheet>
    </>
  );
}
