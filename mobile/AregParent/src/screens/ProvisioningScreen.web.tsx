import { Pressable, StyleSheet, Text, View } from 'react-native';
import { LinkedDevice } from '../api';
import { t } from '../i18n';
import { useLang } from '../useLang';
import { theme } from '../theme';

type Props = {
  device: LinkedDevice;
  onBack: () => void;
};

// Web (and any build without the native BLE module) cannot do Bluetooth
// provisioning. Metro picks this file on web so the native ESP module is never
// bundled there. Shows how to set up Wi-Fi for now.
export default function ProvisioningScreen({ device, onBack }: Props) {
  useLang();
  return (
    <View style={styles.container}>
      <Pressable onPress={onBack}>
        <Text style={styles.back}>{t('back_settings')}</Text>
      </Pressable>
      <Text style={styles.title}>{t('wifi_btn')}</Text>
      <View style={styles.card}>
        <Text style={styles.body}>{t('wifi_web_note')}</Text>
        <Text style={styles.code}>areg-pair</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, padding: 16, paddingTop: 56, backgroundColor: theme.surface },
  back: { color: theme.brand, fontSize: 15, marginBottom: 4 },
  title: { fontSize: 22, fontWeight: '700', color: theme.brand, marginBottom: 16 },
  card: { borderWidth: 1, borderColor: theme.line, borderRadius: 10, padding: 16, backgroundColor: theme.surfaceSunken },
  body: { color: theme.inkMuted, fontSize: 15, marginBottom: 6 },
  strong: { color: theme.brand, fontSize: 16, fontWeight: '700', marginVertical: 2 },
  code: { fontSize: 18, fontWeight: '700', color: theme.warn, marginTop: 4 },
});
