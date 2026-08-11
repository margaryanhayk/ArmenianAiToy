import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { t } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

/**
 * "What Areg can do" — every capability in plain words, each with a real
 * example of what the child actually hears.
 *
 * The owner's own sentence: "I know everything we have, but I am confused
 * using the app. App must show every feature that we have." Nothing in either
 * surface ever told a parent what the toy does; every capability had to be
 * inferred from a switch, and a row labelled "Story pauses" teaches nothing to
 * someone who has never stood next to the toy while it talks.
 *
 * The strings are the web dashboard's, shared rather than rewritten, so the
 * two surfaces cannot answer the same question differently.
 */

type Cap = { k: string; live: boolean };

/**
 * Live vs coming-soon mirrors the web exactly. The four at the bottom are
 * built and switched OFF in the firmware, so claiming them would be a lie and
 * hiding them would be a different one.
 */
const CAPABILITIES: Cap[] = [
  { k: 'stories', live: true },
  { k: 'askmid', live: true },
  { k: 'askafter', live: true },
  { k: 'games', live: true },
  { k: 'riddles', live: true },
  { k: 'why', live: true },
  { k: 'calm', live: true },
  { k: 'greets', live: true },
  { k: 'intro', live: true },
  { k: 'quiet', live: true },
  { k: 'pauses', live: false },
  { k: 'endings', live: false },
  { k: 'buttons', live: false },
  { k: 'music', live: false },
];

function Row({ k }: { k: string }) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowTitle}>{t(`cap_${k}_t` as never)}</Text>
      <Text style={styles.rowExample}>{t(`cap_${k}_e` as never)}</Text>
    </View>
  );
}

export default function CapabilitiesScreen({ onBack }: { onBack: () => void }) {
  useLang();
  const live = CAPABILITIES.filter((c) => c.live);
  const soon = CAPABILITIES.filter((c) => !c.live);

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ paddingBottom: 40 }}>
      <Text style={styles.back} onPress={onBack}>
        {t('back_plain')}
      </Text>
      <Text style={styles.title}>{t('caps_title')}</Text>
      <Text style={styles.intro}>{t('caps_intro')}</Text>

      {live.map((c) => (
        <Row key={c.k} k={c.k} />
      ))}

      <Text style={styles.soonTitle}>{t('caps_soon_title')}</Text>
      <Text style={styles.intro}>{t('caps_soon_note')}</Text>
      {/* Deliberately NOT dimmed. The heading above already says these are not
          on the toy yet; dimming says it a second time and charges legibility
          for the repetition - which is exactly what put the web's version
          under the contrast floor. */}
      {soon.map((c) => (
        <Row key={c.k} k={c.k} />
      ))}

      <Text style={styles.footer}>{t('caps_footer')}</Text>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand, marginBottom: 6 },
  intro: { fontSize: 13, lineHeight: 20, color: theme.inkMuted, marginBottom: 14 },
  row: { paddingVertical: 12, borderBottomWidth: 1, borderBottomColor: theme.lineSoft },
  rowTitle: { fontSize: 15, fontWeight: '700', color: theme.ink, lineHeight: 21 },
  rowExample: { fontSize: 13, lineHeight: 20, color: theme.inkMuted, marginTop: 3 },
  soonTitle: { fontSize: 15, fontWeight: '700', color: theme.ink, marginTop: 24, marginBottom: 4 },
  footer: { fontSize: 13, lineHeight: 20, color: theme.inkMuted, marginTop: 20 },
});
