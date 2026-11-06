import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon, LogoMark } from '../components/icons';
import type { IconName } from '../components/icons';
import {
  Button,
  Card,
  ChipGroup,
  ClickableRow,
  DateQuickPick,
  DayCell,
  DayLegend,
  Drawer,
  EmptyState,
  FieldShell,
  InfoBanner,
  LookupRow,
  Pager,
  Pill,
  SectionHeading,
  Segmented,
  Sheet,
  Stepper,
  TableCard,
  TextArea,
  TextInput,
  YesNo,
  tdClass,
  thClass,
} from '../components/ui';
import { todayIso } from '../components/DateField';
import { getModePreference, setModePreference } from '../theme/mode';
import type { ModePreference } from '../theme/mode';

/**
 * Development only (rendered from App.tsx only when import.meta.env.DEV): every redesign
 * component on one page, to check them at 390x844 and 1440x900 in ar/en x light/dark before
 * the screens are built from them. Not part of the production build.
 */
export function DevGallery() {
  const { t, i18n } = useTranslation();
  const [weather, setWeather] = useState<string | null>('heat');
  const [yes, setYes] = useState<boolean | null>(null);
  const [crew, setCrew] = useState('24');
  const [date, setDate] = useState(todayIso());
  const [sheet, setSheet] = useState(false);
  const [drawer, setDrawer] = useState(false);
  const [pref, setPref] = useState<ModePreference>(getModePreference());
  const icons: IconName[] = ['home', 'clipboard', 'list', 'sliders', 'chevronForward', 'chevronBack', 'arrowForward', 'arrowBack', 'send', 'camera', 'calendar', 'pin', 'search', 'lock', 'logout', 'bell', 'wifiOff', 'clock', 'cloudCheck', 'alert'];

  return (
    <div className="min-h-screen bg-bg p-5 text-ink">
      <div className="mx-auto flex max-w-[720px] flex-col gap-6">
        <div className="flex items-center gap-3">
          <LogoMark size={72} />
          <LogoMark size={34} />
          <span className="text-[30px] font-extrabold">{'Asas'}</span>
        </div>
        <Segmented
          label="language"
          value={i18n.language.startsWith('ar') ? 'ar' : 'en'}
          onChange={(v) => void i18n.changeLanguage(v)}
          options={[{ value: 'ar', label: t('settingsV2.languageArabic') }, { value: 'en', label: t('settingsV2.languageEnglish') }]}
        />
        <Segmented
          label="mode"
          value={pref}
          onChange={(v) => { setPref(v); setModePreference(v); }}
          options={[
            { value: 'light', label: t('settingsV2.appearanceLight') },
            { value: 'dark', label: t('settingsV2.appearanceDark') },
            { value: 'system', label: t('settingsV2.appearanceSystem') },
          ]}
        />
        <div className="flex flex-wrap gap-3 text-ink">{icons.map((n) => <Icon key={n} name={n} size={22} />)}</div>

        <FieldShell label="المشروع / Project" required>
          {({ inputId, describedBy, invalid }) => (
            <LookupRow id={inputId} value="مشروع تجريبي" sub={`DEMO-01 · ${t('steps.lastChoice')}`} placeholder={t('lookup.choose', { label: 'project' })} onOpen={() => setSheet(true)} invalid={invalid} describedBy={describedBy} />
          )}
        </FieldShell>
        <FieldShell label="تاريخ التقرير / Report date" required asGroup>
          {() => <DateQuickPick value={date} onChange={setDate} maxDate={todayIso()} />}
        </FieldShell>
        <FieldShell label="Workers present today" required hint={t('steps.yesterdayValue', { value: 22 })} error={/^\d+$/.test(crew) ? null : t('fieldRules.wholeNumber')}>
          {({ inputId, describedBy, invalid }) => <Stepper id={inputId} value={crew} onChange={setCrew} invalid={invalid} describedBy={describedBy} min={0} />}
        </FieldShell>
        <FieldShell label="Weather" required asGroup>
          {({ labelId }) => (
            <ChipGroup labelledBy={labelId} value={weather} onChange={setWeather} options={[
              { value: 'clear', label: 'صافٍ / Clear' }, { value: 'heat', label: 'حر شديد / Extreme heat' }, { value: 'sand', label: 'Sandstorm' }, { value: 'rain', label: 'Rain' }, { value: 'other', label: 'Other' },
            ]} />
          )}
        </FieldShell>
        <FieldShell label="Was all planned work done?" required asGroup error={yes === null ? 'Required' : null}>
          {({ labelId, invalid }) => <YesNo labelledBy={labelId} value={yes} onChange={setYes} yesLabel={t('common.yes')} noLabel={t('common.no')} invalid={invalid} />}
        </FieldShell>
        <FieldShell label="Delay cause" required>
          {({ inputId }) => <TextArea id={inputId} placeholder="…" />}
        </FieldShell>
        <FieldShell label="Email">
          {({ inputId }) => <TextInput id={inputId} type="email" dir="ltr" defaultValue="hazem@company.sa" />}
        </FieldShell>

        <div className="flex flex-col gap-3">
          <Button block>{t('steps.next', { title: 'Progress' })}</Button>
          <Button block variant="success" icon={<Icon name="send" size={20} />}>{t('steps.send')}</Button>
          <Button block variant="secondary">{t('steps.previous')}</Button>
          <Button block variant="danger">Deactivate</Button>
          <div className="flex gap-2">
            <Button size="desktop">Desktop primary</Button>
            <Button size="desktop" variant="secondary">Secondary</Button>
            <Button variant="link">{t('today.view')}</Button>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <Pill tone="success">Filled</Pill><Pill tone="warning">{t('today.dueToday')}</Pill><Pill tone="danger">Plan not met</Pill><Pill tone="info">2 photos</Pill><Pill tone="neutral">Ended</Pill>
        </div>
        <InfoBanner>{t('formBuilder.liveEditNote', 'Info banner')}</InfoBanner>
        <InfoBanner tone="warning">Telegram not connected.</InfoBanner>
        <EmptyState title="No problems reported" body="Since Jan 20." />

        <Card className="flex flex-col gap-3 p-5">
          <SectionHeading>{t('today.lastTwoWeeks')}</SectionHeading>
          <div className="flex gap-[6px]">
            {(['filled', 'filled', 'missed', 'filled', 'off', 'filled', 'due'] as const).map((s, i) => <DayCell key={i} state={s} label={`${i}: ${s}`} size={36} />)}
          </div>
          <DayLegend showOff />
        </Card>

        <TableCard footer={<Pager from={1} to={2} total={46} onNext={() => {}} />}>
          <thead><tr><th className={thClass}>Date</th><th className={thClass}>Project</th><th className={thClass}>Plan</th></tr></thead>
          <tbody>
            <ClickableRow selected onOpen={() => setDrawer(true)}><td className={tdClass}><b>5 Nov</b></td><td className={tdClass}>119</td><td className={tdClass}><Pill tone="success">Met</Pill></td></ClickableRow>
            <ClickableRow onOpen={() => setDrawer(true)}><td className={tdClass}><b>4 Nov</b></td><td className={tdClass}>119</td><td className={tdClass}><Pill tone="danger">Not met</Pill></td></ClickableRow>
          </tbody>
        </TableCard>
      </div>

      <Sheet open={sheet} onClose={() => setSheet(false)} title={t('lookup.choose', { label: 'project' })} footer={<p className="text-center text-[13px] text-ink-soft">{t('lookup.notFound')}</p>}>
        <TextInput placeholder={t('lookup.search')} />
        <p className="mt-4 text-[13px] font-bold text-ink-soft">{t('lookup.recent')}</p>
      </Sheet>
      <Drawer open={drawer} onClose={() => setDrawer(false)} title="Thursday 5 November" sub={t('records.sentByAt', { name: 'Hazim', time: '3:55 PM' })} footer={<><Button size="desktop" block>Edit</Button><Button size="desktop" variant="danger">Delete</Button></>}>
        <div className="p-5">Rows</div>
      </Drawer>
    </div>
  );
}
