import { Pressable, StyleSheet, Text, View } from 'react-native';
import { t } from '../i18n';
import { theme } from '../theme';

export type DiaryView = 'conversations' | 'plays' | 'flagged';

/**
 * "What happened?" had three answers in three places: a Conversations screen,
 * a Flagged link hidden in its header, and a Stories-listened-to screen
 * reached from a different card entirely. A parent had to already know a
 * screen existed in order to find it.
 *
 * One control, on all three, showing where you are and reaching the other two
 * in a tap. The lists stay separate because they ARE separate - three
 * endpoints, three shapes - and interleaving them would either lie about the
 * order or drop rows. What was needed was one place to ASK, not one list.
 *
 * Pills rather than the web's dropdown: three options is under the threshold
 * where a picker earns its extra tap, and a phone has the width for them.
 */
export default function DiaryFilter({
  current,
  onChange,
}: {
  current: DiaryView;
  onChange: (v: DiaryView) => void;
}) {
  const items: [DiaryView, string][] = [
    ['conversations', t('diary_talk')],
    ['plays', t('diary_heard')],
    ['flagged', t('diary_held')],
  ];
  return (
    <View style={styles.row}>
      {items.map(([v, label]) => {
        const on = v === current;
        return (
          <Pressable
            key={v}
            style={[styles.pill, on && styles.pillOn]}
            onPress={() => (on ? undefined : onChange(v))}
            accessibilityRole="tab"
            // BOTH, and they are not redundant: accessibilityState is what
            // iOS and Android read, and react-native-web does not translate it
            // into aria-selected - the web build emitted role=tab with no
            // selected state at all, so a screen reader there could not tell
            // which of the three you were on.
            accessibilityState={{ selected: on }}
            aria-selected={on}
          >
            <Text style={[styles.pillText, on && styles.pillTextOn]} numberOfLines={1}>
              {label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: 6, marginBottom: 10 },
  pill: {
    flex: 1,
    paddingVertical: 8,
    paddingHorizontal: 6,
    borderRadius: 999,
    borderWidth: 1,
    borderColor: theme.line,
    backgroundColor: theme.surface,
    alignItems: 'center',
  },
  pillOn: { borderColor: theme.brand, backgroundColor: theme.brandTint },
  pillText: { fontSize: 12.5, color: theme.inkMuted, fontWeight: '600' },
  pillTextOn: { color: theme.brand },
});
